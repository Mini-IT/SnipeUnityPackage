using kcp2k;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace MiniIT.Snipe
{
	public class KcpConnection
	{
		private enum KcpState { Connected, Authenticated, Disconnected }

		private class ChunkedMessageItem
		{
			public byte[] buffer;
			public int length;
		}

		private const int CHANNEL_HEADER_SIZE = 1;
		public const int PING_INTERVAL = 1000;
		public const int MAX_PINGLESS_INTERVAL = 5 * 60000 - PING_INTERVAL * 2; // 5 minutes
		private const int KCP_SEND_WINDOW_SIZE = 4096;    // Kcp.WND_SND; 32 by default
		private const int KCP_RECEIVE_WINDOW_SIZE = 4096; // Kcp.WND_RCV; 128 by default
		private const int QUEUE_DISCONNECT_THRESHOLD = 10000;
		private const int MAX_KCP_MESSAGE_SIZE = Kcp.MTU_DEF - Kcp.OVERHEAD;
		private const int CHUNK_DATA_SIZE = MAX_KCP_MESSAGE_SIZE - 5; // channel (1 byte) + header (4 bytes): KcpHeader.Chunk + msg_id + chunk_id + num_chunks

		// reliable channel (= kcp) MaxMessageSize so the outside knows largest
		// allowed message to send. the calculation in Send() is not obvious at
		// all, so let's provide the helper here.
		//
		// kcp does fragmentation, so max message is way larger than MTU.
		//
		// -> runtime MTU changes are disabled: mss is always MTU_DEF-OVERHEAD
		// -> Send() checks if fragment count < rcv_wnd, so we use rcv_wnd - 1.
		//    NOTE that original kcp has a bug where WND_RCV default is used
		//    instead of configured rcv_wnd, limiting max message size to 144 KB
		//    https://github.com/skywind3000/kcp/pull/291
		//    we fixed this in kcp2k.
		// -> we add 1 byte KcpHeader enum to each message, so -1
		//
		// IMPORTANT: max message is MTU * rcv_wnd, in other words it completely
		//            fills the receive window! due to head of line blocking,
		//            all other messages have to wait while a maxed size message
		//            is being delivered.
		//            => in other words, DO NOT use max size all the time like
		//               for batching.
		//            => sending UNRELIABLE max message size most of the time is
		//               best for performance (use that one for batching!)
		//private static int ReliableMaxMessageSize_Unconstrained(uint rcv_wnd) => (Kcp.MTU_DEF - Kcp.OVERHEAD - CHANNEL_HEADER_SIZE) * ((int)rcv_wnd - 1) - 1;

		// kcp encodes 'frg' as 1 byte.
		// max message size can only ever allow up to 255 fragments.
		//   WND_RCV gives 127 fragments.
		//   WND_RCV * 2 gives 255 fragments.
		// so we can limit max message size by limiting rcv_wnd parameter.
		//private static int ReliableMaxMessageSize(uint rcv_wnd) => ReliableMaxMessageSize_Unconstrained(Math.Min(rcv_wnd, Kcp.FRG_MAX));

		// unreliable max message size is simply MTU - channel header size
		private const int UnreliableMaxMessageSize = Kcp.MTU_DEF - CHANNEL_HEADER_SIZE;

		public Action OnAuthenticated;
		public Action<ArraySegment<byte>, KcpChannel, bool> OnData;
		public Action OnDisconnected;

		public bool Connected => _socket != null && _socket.Connected && _state == KcpState.Authenticated;

		private UdpSocketWrapper _socket;
		private Kcp _kcp;

		private KcpState _state = KcpState.Disconnected;

		private readonly byte[] _socketReceiveBuffer = new byte[Kcp.MTU_DEF]; // MTU must fit channel + message!
		private readonly byte[] _socketSendBuffer = new byte[Kcp.MTU_DEF];
		private byte[] _kcpReceiveBuffer;
		private byte[] _kcpSendBuffer;

		private Dictionary<byte, ChunkedMessageItem> _chunkedMessages;
		private byte _chunkedMessageId = 0;

		// If we don't receive anything these many milliseconds
		// then consider us disconnected
		private int _timeout;
		private int _authenticationTimeout;
		private uint _lastReceiveTime;

		// internal timer
		private readonly Stopwatch _refTime = new Stopwatch();

		private uint _lastPingTime;
		private uint _pingsCount = 0;
		public uint PingTime { get; private set; }
		
		private readonly object _kcpLock = new object();
		private readonly object _kcpSendBufferLock = new object();
		private readonly object _socketSendBufferLock = new object();

		public void Connect(string host, ushort port, int timeout = 10000, int authenticationTimeout = 0)
		{
			if (_socket != null)
				return;

			_timeout = timeout;
			_authenticationTimeout = authenticationTimeout > 0 ? authenticationTimeout : timeout;
			
			_state = KcpState.Disconnected;

			_socket = new UdpSocketWrapper();
			_socket.OnConnected += OnSocketConnected;
			_socket.OnDisconnected += OnSocketDisconnected;
			_socket.Connect(host, port);
		}

		private void OnSocketConnected()
		{
			lock (_kcpLock)
			{
				// Create a new Kcp instance
				// even if _kcp != null its buffers may content some data from the previous connection
				_kcp = new Kcp(0, SocketSendReliable);

				_kcp.SetNoDelay(1u, // NoDelay is recommended to reduce latency
					10,             // internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities
					2,              // fastresend. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode
					true);          // no congestion window. Congestion window is enabled in normal mode, disabled in turbo mode.

				_kcp.SetWindowSize(KCP_SEND_WINDOW_SIZE, KCP_RECEIVE_WINDOW_SIZE);

				// IMPORTANT: high level needs to add 1 channel byte to each raw
				// message. so while Kcp.MTU_DEF is perfect, we actually need to
				// tell _kcp to use MTU-1 so we can still put the header into the
				// message afterwards.
				_kcp.SetMtu(Kcp.MTU_DEF - CHANNEL_HEADER_SIZE);

				// set maximum retransmits (aka dead_link)
				// KCP will try to retransmit lost messages up to MaxRetransmit (aka dead_link) before disconnecting. default prematurely disconnects a lot of people (#3022). use 2x
				_kcp.dead_link = Kcp.DEADLINK * 2;
			}

			lock (_kcpSendBufferLock)
			{
				// create message buffers AFTER window size is set
				// see comments on buffer definition for the "+1" part
				//int bufferSize = 1 + ReliableMaxMessageSize(KCP_RECEIVE_WINDOW_SIZE);

				// Server side implementation does not support message fragmentation.
				// That is why we use custom algorythm of breaking large messages into chunks.
				// In this case we don't need buffers larger than MAX_KCP_MESSAGE_SIZE
				int bufferSize = MAX_KCP_MESSAGE_SIZE;
				_kcpReceiveBuffer = new byte[bufferSize];
				_kcpSendBuffer = new byte[bufferSize];

				_chunkedMessages = new Dictionary<byte, ChunkedMessageItem>(1);
			}

			_state = KcpState.Connected;
			DebugLogger.Log("KcpState.Connected");

			_refTime.Start();

			SendReliable(KcpHeader.Handshake);
			
			// force sending handshake immediately
			TickOutgoing();
		}

		private void OnSocketDisconnected()
		{
			_state = KcpState.Disconnected;
			OnDisconnected?.Invoke();
		}

		public void Disconnect()
		{
			// only if not disconnected yet
			if (_state == KcpState.Disconnected)
				return;

			// send a disconnect message
			if (_socket != null && _socket.Connected)
			{
				try
				{
					SendReliable(KcpHeader.Disconnect);
					
					lock (_kcpLock)
					{
						_kcp.Flush();
					}
				}
				//catch (SocketException)
				//{
				//	// this is ok, the connection was already closed
				//}
				//catch (ObjectDisposedException)
				//{
				//	// this is normal when we stop the server
				//	// the socket is stopped so we can't send anything anymore
				//	// to the clients
				//
				//	// the clients will eventually timeout and realize they
				//	// were disconnected
				//}
				catch (Exception)
				{
					// ignore
				}
			}

			// set as Disconnected, call event
			DebugLogger.Log("KCP Connection: Disconnected.");
			_state = KcpState.Disconnected;
			OnDisconnected?.Invoke();

			DisposeSocket();
		}

		public void SendData(ArraySegment<byte> data, KcpChannel channel)
		{
			if (_state == KcpState.Disconnected)
			{
				DebugLogger.LogWarning("KcpConnection: tried sending while disconnected");
				return;
			}
			
			if (data.Count == 0)
			{
				DebugLogger.LogWarning("KcpConnection: tried sending empty message");
				return;
			}

			// 1 byte header + content
			if (1 + data.Count > MAX_KCP_MESSAGE_SIZE)
			{
				if (channel == KcpChannel.Reliable)
				{
					byte num_chunks = (byte)(data.Count / CHUNK_DATA_SIZE + 1);

					byte[] buffer = ArrayPool<byte>.Shared.Rent(Kcp.MTU_DEF - 1); // without kcp header
					buffer[0] = (++_chunkedMessageId);
					buffer[2] = num_chunks;

					for (int chunk_id = 0; chunk_id < num_chunks; chunk_id++)
					{
						int start_index = chunk_id * CHUNK_DATA_SIZE;
						int length = (start_index + CHUNK_DATA_SIZE > data.Count) ? data.Count % CHUNK_DATA_SIZE : CHUNK_DATA_SIZE;

						buffer[1] = (byte)chunk_id;
						Buffer.BlockCopy(data.Array, data.Offset + start_index, buffer, 3, length);

						var chunk_data = new ArraySegment<byte>(buffer, 0, length + 3);
						SendReliable(KcpHeader.Chunk, chunk_data);
					}
					ArrayPool<byte>.Shared.Return(buffer);
				}
				else
				{
					DebugLogger.LogWarning("KcpConnection: message size is larger than MTU. This is not supported by unreliable channel.");
				}
			}
			else
			{
				switch (channel)
				{
					case KcpChannel.Reliable:
						SendReliable(KcpHeader.Data, data);
						break;
					case KcpChannel.Unreliable:
						SendUnreliable(data);
						break;
				}
			}
		}

		public void SendBatchReliable(List<ArraySegment<byte>> data)
		{
			if (_state == KcpState.Disconnected)
			{
				DebugLogger.LogWarning("KcpConnection: tried sending while disconnected");
				return;
			}
			
			if (data.Count == 0)
			{
				DebugLogger.LogWarning("KcpConnection: tried sending empty batch");
				return;
			}
			
			int length = 0;
			int startMessageIndex = 0;
			for (int i = 0; i < data.Count; i++)
			{
				int msgLen = data[i].Count + 3; // 3 bytes for length

				// 1 byte header + content
				if (1 + length + msgLen <= MAX_KCP_MESSAGE_SIZE)
				{
					length += msgLen;
				}
				else
				{
					DebugLogger.LogWarning("KcpConnection - SendBatchReliable: batch size is larger than MTU");

					if (length > 0) // if there is any cumulated data to send
					{
						int messagesCount = i - startMessageIndex + 1;
						SendBatchData(data, startMessageIndex, messagesCount, length);
					}
					else // the first message in the batch is larger than MTU
					{
						SendData(data[startMessageIndex], KcpChannel.Reliable);
					}

					startMessageIndex = i + 1;
					length = 0;
				}
			}

			if (length > 0) // if there is any acumulated data to send
			{
				int messagesCount = data.Count - startMessageIndex;
				SendBatchData(data, startMessageIndex, messagesCount, length);
			}
		}

		private void SendBatchData(List<ArraySegment<byte>> data, int startIndex, int count, int bufferLength)
		{
			byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
			int offset = 0;
				
			for (int i = 0; i < count; i++)
			{
				int index = startIndex + i;
				var messageData = data[index];
				BytesUtil.WriteInt3(buffer, offset, messageData.Count);
				offset += 3;
				Buffer.BlockCopy(messageData.Array, messageData.Offset, buffer, offset, messageData.Count);
				offset += messageData.Count;
			}

			// NOTE: at this point offset must be equal to bufferLength

			SendReliable(KcpHeader.Batch, new ArraySegment<byte>(buffer, 0, offset));
				
			ArrayPool<byte>.Shared.Return(buffer);
		}

		public void Tick()
		{
			TickIncoming();
			TickOutgoing();
		}

		public void TickIncoming()
		{
			SocketReceive();

			if (_state == KcpState.Disconnected || _kcp == null)
				return;

			uint time = (uint)_refTime.ElapsedMilliseconds;

			try
			{
				// detect common events & ping
				HandleTimeout(time);
				HandleDeadLink();
				HandlePing(time);
				HandleChoked();
				
				while (//!paused &&
					ReceiveNextReliable(out KcpHeader header, out ArraySegment<byte> message))
				{
					if (header == KcpHeader.Disconnect)
					{
						DebugLogger.Log("KCP: received disconnect message");
						Disconnect();
						break;
					}

					if (header == KcpHeader.Ping)
					{
						PingTime = time - _lastPingTime;
						continue;
					}

					if (_state == KcpState.Connected)
					{
						if (header == KcpHeader.Handshake)
						{
							// we were waiting for a handshake.
							// it proves that the other end speaks our protocol.
							DebugLogger.Log("KCP: received handshake");
							_state = KcpState.Authenticated;
							OnAuthenticated?.Invoke();
							continue;
						}
						else // nothing else is expected
						{
							// should never receive another handshake after auth
							DebugLogger.LogWarning($"KCP: received invalid header {header} while Connected. Disconnecting the connection.");
							Disconnect();
							break;
						}
					}

					if (_state == KcpState.Authenticated)
					{
						switch (header)
						{
							case KcpHeader.Handshake:
								// should never receive another handshake after auth
								DebugLogger.LogWarning($"KCP: received invalid header {header} while Authenticated. Disconnecting the connection.");
								Disconnect();
								break;

							case KcpHeader.Data:
								HandleReliableData(message);
								break;

							case KcpHeader.Chunk:
								HandleReliableChunk(message, false);
								break;

							case KcpHeader.CompressedChunk:
								HandleReliableChunk(message, true);
								break;
						}
					}
				}
			}
			catch (SocketException exception)
			{
				// this is ok, the connection was closed
				DebugLogger.Log($"KCP Connection: Disconnecting because {exception}. This is fine.");
				Disconnect();
			}
			catch (ObjectDisposedException exception)
			{
				// fine, socket was closed
				DebugLogger.Log($"KCP Connection: Disconnecting because {exception}. This is fine.");
				Disconnect();
			}
			catch (Exception ex)
			{
				// unexpected
				DebugLogger.LogError(ex.ToString());
				Disconnect();
			}
		}

		public void TickOutgoing()
		{
			if (_state == KcpState.Disconnected || _kcp == null)
				return;

			try
			{
				uint time = (uint)_refTime.ElapsedMilliseconds;
				_kcp.Update(time);
			}
			catch (SocketException exception)
			{
				// this is ok, the connection was closed
				DebugLogger.Log($"KCP Connection: Disconnecting because {exception}. This is fine.");
				Disconnect();
			}
			catch (ObjectDisposedException exception)
			{
				// fine, socket was closed
				DebugLogger.Log($"KCP Connection: Disconnecting because {exception}. This is fine.");
				Disconnect();
			}
			catch (Exception ex)
			{
				// unexpected
				DebugLogger.LogError(ex.ToString());
				Disconnect();
			}
		}

		private void SendUnreliable(ArraySegment<byte> message)
		{
			if (message.Count > UnreliableMaxMessageSize)
			{
				DebugLogger.LogError($"Failed to send unreliable message of size {message.Count} because it's larger than UnreliableMaxMessageSize={UnreliableMaxMessageSize}");
				return;
			}

			SocketSend(KcpChannel.Unreliable, message.Array, message.Offset, message.Count);
		}

		private void SendReliable(KcpHeader header, ArraySegment<byte> content = default)
		{
			lock (_kcpSendBufferLock)
			{
				int msgLength = 1 + content.Count; // 1 byte for header
				if (_kcpSendBuffer.Length < msgLength)
				{
					DebugLogger.LogError($"SendReliable failed. Message length ({msgLength} bytes) is greater than the buffer size ({_kcpSendBuffer.Length})");
					return;
				}

				_kcpSendBuffer[0] = (byte)header;
				if (content.Count > 0)
				{
					Buffer.BlockCopy(content.Array, content.Offset, _kcpSendBuffer, 1, content.Count);
				}

				// send to kcp for processing
				int sent = _kcp.Send(_kcpSendBuffer, 0, msgLength);
				if (sent < 0)
				{
					DebugLogger.LogWarning($"Send failed with error={sent} for content with length={content.Count}");
				}
			}
		}

		private void SocketSendReliable(byte[] data, int length)
		{
			SocketSend(KcpChannel.Reliable, data, 0, length);
		}

		private void SocketSend(KcpChannel channel, byte[] data, int offset, int length)
		{
			lock (_socketSendBufferLock)
			{
				// copy channel header, data into raw send buffer, then send
				_socketSendBuffer[0] = (byte)channel;
				if (length > 0)
				{
					Buffer.BlockCopy(data, offset, _socketSendBuffer, 1, length);
				}
				_socket.Send(_socketSendBuffer, length + 1);
			}
		}

		private void SocketReceive()
		{
			try
			{
				if (_socket != null && _state != KcpState.Disconnected)
				{
					while (_socket.Poll(0, SelectMode.SelectRead))
					{
						int msgLength = _socket.Receive(_socketReceiveBuffer);

						// DebugLogger.Log($"RAW RECV {msgLength} bytes = {BitConverter.ToString(_socketReceiveBuffer, 0, msgLength)}");

						// IMPORTANT: detect if buffer was too small for the
						//            received msgLength. otherwise the excess
						//            data would be silently lost.
						//            (see ReceiveFrom documentation)
						if (msgLength <= _socketReceiveBuffer.Length)
						{
							RawInput(_socketReceiveBuffer, msgLength);
						}
						else
						{
							DebugLogger.LogError($"KCP ClientConnection: message of size {msgLength} does not fit into buffer of size {_socketReceiveBuffer.Length}. The excess was silently dropped. Disconnecting.");
							Disconnect();
						}
					}
				}
			}
			catch (SocketException)
			{
				// this is fine, the socket might have been closed in the other end
			}
			catch (ObjectDisposedException)
			{
				Disconnect();
			}
		}

		private void RawInput(byte[] buffer, int msgLength)
		{
			if (msgLength < 1)
				return;

			// DebugLogger.Log($"KCP RawReceive {msgLength} / {buffer.Length}\n{BitConverter.ToString(buffer, 0, msgLength)}");

			byte channel = buffer[0];
			switch (channel)
			{
				case (byte)KcpChannel.Reliable:
					// input into kcp, but skip channel byte
					int input = 0;
					
					lock (_kcpLock)
					{
						input = _kcp.Input(buffer, 1, msgLength - 1);
					}
					
					if (input != 0)
					{
						DebugLogger.LogWarning($"KCP Input failed with error={input} for buffer with length={msgLength - 1}");
					}
					break;

				case (byte)KcpChannel.Unreliable:
					// ideally we would queue all unreliable messages and
					// then process them in ReceiveNext() together with the
					// reliable messages, but:
					// -> queues/allocations/pools are slow and complex.
					// -> DOTSNET 10k is actually slower if we use pooled
					//    unreliable messages for transform messages.
					//
					//      DOTSNET 10k benchmark:
					//        reliable-only:         170 FPS
					//        unreliable queued: 130-150 FPS
					//        unreliable direct:     183 FPS(!)
					//
					//      DOTSNET 50k benchmark:
					//        reliable-only:         FAILS (queues keep growing)
					//        unreliable direct:     18-22 FPS(!)
					//
					// -> all unreliable messages are DATA messages anyway.
					// -> let's skip the magic and call OnData directly if
					//    the current state allows it.
					if (_state == KcpState.Authenticated)
					{
						// only process messages while not paused for Mirror
						// scene switching etc.
						// -> if an unreliable message comes in while
						//    paused, simply drop it. it's unreliable!
						//if (!paused)
						{
							ArraySegment<byte> message = new ArraySegment<byte>(buffer, 1, msgLength - 1);
							OnData?.Invoke(message, KcpChannel.Unreliable, false);
						}

						// set last receive time to avoid timeout.
						// -> we do this in ANY case even if not enabled.
						//    a message is a message.
						// -> we set last receive time for both reliable and
						//    unreliable messages. both count.
						//    otherwise a connection might time out even
						//    though unreliable were received, but no
						//    reliable was received.
						//UpdateLastReceiveTime();
					}
					else
					{
						// should never
						DebugLogger.LogWarning($"KCP: received unreliable message in state {_state}. Disconnecting the connection.");
						Disconnect();
					}
					break;

				default:
					// not a valid channel. random data or attacks.
					DebugLogger.LogWarning($"Invalid KCP channel header: {channel}");
					//DebugLogger.Log($"Disconnecting connection because of invalid channel header: {channel}");
					//Disconnect();
					break;
			}

			UpdateLastReceiveTime();
		}

		private bool ReceiveNextReliable(out KcpHeader header, out ArraySegment<byte> message)
		{
			int msgSize = _kcp.PeekSize();
			if (msgSize > 0)
			{
				// only allow receiving up to buffer sized messages.
				// otherwise we would get BlockCopy ArgumentException anyway.
				if (msgSize <= _kcpReceiveBuffer.Length)
				{
					// receive from kcp
					int received = _kcp.Receive(_kcpReceiveBuffer, msgSize);
					if (received >= 0)
					{
						// extract header & content without header
						header = (KcpHeader)_kcpReceiveBuffer[0];
						message = new ArraySegment<byte>(_kcpReceiveBuffer, 1, msgSize - 1);
						UpdateLastReceiveTime();

						// DebugLogger.Log($"KCP: raw recv {received} header = {header} bytes ({message.Count}) = {BitConverter.ToString(message.Array, message.Offset, message.Count)}");

						return true;
					}
					else
					{
						// if receive failed, close everything
						DebugLogger.LogWarning($"Receive failed with error={received}. closing connection.");
						Disconnect();
					}
				}
				// we don't allow sending messages > Max, so this must be an
				// attacker. let's disconnect to avoid allocation attacks etc.
				else
				{
					DebugLogger.LogWarning($"KCP: possible allocation attack for msgSize {msgSize} > buffer {_kcpReceiveBuffer.Length}. Disconnecting the connection.");
					Disconnect();
				}
			}

			message = default;
			header = KcpHeader.Disconnect;
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void UpdateLastReceiveTime()
		{
			_lastReceiveTime = (uint)_refTime.ElapsedMilliseconds;
			_pingsCount = 0;
		}

		private void HandleReliableData(ArraySegment<byte> message)
		{
			DebugLogger.Log("HandleReliableData");

			// call OnData IF the message contained actual data
			if (message.Count > 0)
			{
				//DebugLogger.LogWarning($"Kcp recv msg: {BitConverter.ToString(message.Array, message.Offset, message.Count)}");
				OnData?.Invoke(message, KcpChannel.Reliable, false);
			}
			else // empty data = attacker, or something went wrong
			{
				DebugLogger.LogWarning("KCP: received empty Data message while Authenticated. Disconnecting the connection.");
				Disconnect();
			}
		}

		private void HandleReliableChunk(ArraySegment<byte> message, bool compressed)
		{
			DebugLogger.Log("HandleReliableChunk");

			// call OnData IF the message contained actual data
			if (message.Count > 3)
			{
				byte message_id = message.Array[message.Offset];
				byte chunk_id = message.Array[message.Offset + 1];
				byte chunks_count = message.Array[message.Offset + 2];

				// Log.Info($"[KcpConnection] CHUNK RECEIVED {message_id} - {chunk_id} / {chunks_count}  {message.Count} bytes");

				// TODO: check values

				const int MAX_CHUNK_SIZE = Kcp.MTU_DEF - Kcp.OVERHEAD - 5; // channel (1 byte) + KcpHeader.Chunk + msg_id + chunk_id + num_chunks

				ChunkedMessageItem item;
				if (_chunkedMessages.ContainsKey(message_id))
				{
					item = _chunkedMessages[message_id];
				}
				else
				{
					item = new ChunkedMessageItem()
					{
						buffer = ArrayPool<byte>.Shared.Rent(chunks_count * MAX_CHUNK_SIZE + 5), // opcode (1 byte) + message length (4 bytes)
						length = 0,
					};
					_chunkedMessages[message_id] = item;
				}

				// message header (3 bytes): msg_id + chunk_id + num_chunks
				Buffer.BlockCopy(message.Array, message.Offset + 3, item.buffer, 5 + chunk_id * MAX_CHUNK_SIZE, message.Count - 3);
				item.length += message.Count - 3;

				if (item.length > (1 + (chunks_count - 1) * MAX_CHUNK_SIZE)) // all chunks
				{
					// DebugLogger.Log($"[KcpConnection] CHUNKED_MESSAGE received: {BitConverter.ToString(item.buffer, 0, item.buffer.Length)}");

					// opcode Snipe Response
					item.buffer[0] = (byte)KcpOpCodes.SnipeResponse;

					// length (4 bytes int)
					BytesUtil.WriteInt(item.buffer, 1, item.length);

					OnData?.Invoke(new ArraySegment<byte>(item.buffer, 0, item.length + 5), KcpChannel.Reliable, compressed);
					ArrayPool<byte>.Shared.Return(item.buffer);
					_chunkedMessages.Remove(message_id);
				}
			}
			// empty data = attacker, or something went wrong
			else
			{
				DebugLogger.LogWarning("KCP: received empty Chunk message while Authenticated. Disconnecting the connection.");
				Disconnect();
			}
		}

		private void HandleTimeout(uint time)
		{
			int timeout = (_state == KcpState.Authenticated) ? _timeout : _authenticationTimeout;

			// note: we are also sending a ping regularly, so timeout should
			//       only ever happen if the connection is truly gone.
			if (time >= _lastReceiveTime + timeout)
			{
				if (_state == KcpState.Authenticated && _pingsCount < 3 && time < _lastPingTime + MAX_PINGLESS_INTERVAL)
				{
					DebugLogger.Log($"KCP: HandleTimeout - Waiting for ping {_pingsCount}");
					return;
				}

				DebugLogger.LogWarning($"KCP: Connection timed out after not receiving any message for {timeout}ms. Disconnecting.");
				Disconnect();
			}
		}

		private void HandleDeadLink()
		{
			// kcp has 'dead_link' detection. might as well use it.
			if (_kcp.GetState() == -1)
			{
				DebugLogger.LogWarning($"KCP Connection dead_link detected: a message was retransmitted {_kcp.dead_link} times without ack. Disconnecting.");
				Disconnect();
			}
		}

		// send a ping occasionally in order to not time out on the other end.
		private void HandlePing(uint time)
		{
			// enough time elapsed since last ping?
			if (time >= _lastPingTime + PING_INTERVAL)
			{
				// DebugLogger.Log("kcp send ping");
				SendReliable(KcpHeader.Ping);
				_lastPingTime = time;
				_pingsCount++;
			}
		}

		private void HandleChoked()
		{
			// disconnect connections that can't process the load.
			// see QueueSizeDisconnect comments.
			// => include all of kcp's buffers and the unreliable queue!
			int total = _kcp.GetRcvQueueCount() + _kcp.GetSndQueueCount() +
						_kcp.GetRcvBufCount() + _kcp.GetSndBufCount();
			if (total >= QUEUE_DISCONNECT_THRESHOLD)
			{
				DebugLogger.LogWarning($"KCP: disconnecting connection because it can't process data fast enough.\n" +
								 $"Queue total {total}>{QUEUE_DISCONNECT_THRESHOLD}. rcv_queue={_kcp.GetRcvQueueCount()} snd_queue={_kcp.GetSndQueueCount()} rcv_buf={_kcp.GetRcvBufCount()} snd_buf={_kcp.GetSndBufCount()}\n" +
								 $"* Try to Enable NoDelay, decrease INTERVAL, disable Congestion Window (= enable NOCWND!), increase SEND/RECV WINDOW or compress data.\n" +
								 $"* Or perhaps the network is simply too slow on our end, or on the other end.\n");

				// let's clear all pending sends before disconnting with 'Bye'.
				// otherwise a single Flush in Disconnect() won't be enough to
				// flush thousands of messages to finally deliver 'Bye'.
				// this is just faster and more robust.
				_kcp.ClearSndQueue();

				Disconnect();
			}
		}

		private void DisposeSocket()
		{
			if (_socket != null)
			{
				_socket.OnConnected -= OnSocketConnected;
				_socket.OnDisconnected -= OnSocketDisconnected;
				_socket.Dispose();
				_socket = null;
			}
		}
	}
}
