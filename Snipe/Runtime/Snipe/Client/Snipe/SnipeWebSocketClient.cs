using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;
using Ionic.Zlib;
using MiniIT;

//
// http://snipeserver.com
// https://github.com/Mini-IT/SnipeWiki/wiki


namespace MiniIT.Snipe
{
	internal class SnipeWebSocketClient : SnipeAbstractClient
	{
		private readonly int INSTANCE_ID = new System.Random().Next();
		
		private WebSocketWrapper mWebSocketClient = null;
		
		public void Connect(string url)
		{
			Disconnect();

			DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) WebSocket Connect to " + url);

			mWebSocketClient = new WebSocketWrapper();
			mWebSocketClient.OnConnectionOpened = OnWebSocketConnected;
			mWebSocketClient.OnConnectionClosed = OnWebSocketClose;
			mWebSocketClient.ProcessMessage = ProcessData;
			mWebSocketClient.Connect(url);
		}

		protected void OnWebSocketConnected()
		{
			DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) OnWebSocketConnected");
			
			mConnected = true;

			OnConnectionSucceeded?.Invoke();
		}
		
		protected void OnWebSocketClose()
		{
			DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) OnWebSocketClose");

			if (this.mConnected)
			{
				Disconnect();
				
				if (OnConnectionLost != null)
				{
					OnConnectionLost?.Invoke();
				}
				else
				{
					OnConnectionFailed?.Invoke();
				}
			}
			else
			{
				Disconnect();
				
				OnConnectionFailed?.Invoke();
			}
		}

		/*
		protected void OnWebSocketError (object sender, WebSocketSharp.ErrorEventArgs e)
		{
			DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) OnWebSocketError: " + e.Message);
			//DispatchEvent(ErrorHappened);
		}
		*/
	
		public override void Disconnect()
		{
			mConnected = false;
			
			DisposeBuffer();

			if (mWebSocketClient != null)
			{
				lock (mWebSocketClient)
				{
					mWebSocketClient.OnConnectionOpened = null;
					mWebSocketClient.OnConnectionClosed = null;
					mWebSocketClient.ProcessMessage = null;
					mWebSocketClient.Dispose();
				}
				mWebSocketClient = null;
			}
		}

		// Process WebSocket Message
		protected void ProcessData(byte[] raw_data_buffer)
		{
			if (raw_data_buffer != null && raw_data_buffer.Length > 0)
			{
				using (MemoryStream buf_stream = new MemoryStream(raw_data_buffer))
				{
					buf_stream.Position = 0;
					
					try
					{
						// the 1st byte contains compression flag (0/1)
						mCompressed = (buf_stream.ReadByte() == 1);
						mMessageLength = Convert.ToInt32(buf_stream.Length) - 1;

						if (mCompressed)
						{
							byte[] compressed_buffer = new byte[mMessageLength];
							buf_stream.Read(compressed_buffer, 0, compressed_buffer.Length);

							byte[] decompressed_buffer = ZlibStream.UncompressBuffer(compressed_buffer);
							mMessageString = UTF8Encoding.UTF8.GetString( decompressed_buffer );

//							DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) decompressed mMessageString = " + mMessageString);
						}
						else
						{
							byte[] str_buf = new byte[mMessageLength];
							buf_stream.Read(str_buf, 0, mMessageLength);
							mMessageString = UTF8Encoding.UTF8.GetString(str_buf);

//							DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) mMessageString = " + mMessageString);
						}
					}
					catch (Exception)
					{
						//DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) OnWebSocketMessage ProcessData error: " + ex.Message);
						
						//CheckConnectionLost();
					}

					mMessageLength = 0;

					// the message is read

					try
					{
						ExpandoObject response = (ExpandoObject)HaxeUnserializer.Run(mMessageString);

						if (response != null)
						{
							try
							{
								OnMessageReceived?.Invoke(response);
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) ProcessData - OnMessageReceived invokation failed: {e.Message}");
							}
						}
					}
#if DEBUG
					catch (Exception error)
					{
						DebugLogger.Log($"[SnipeWebSocketClient] ({INSTANCE_ID}) Deserialization error: {error.Message}");
#else
					catch (Exception)
					{
#endif
						// if (OnError != null)
						//		OnError(new HapiEventArgs(HapiEventArgs.ERROR, "Deserialization error: " + error.Message));

						// TODO: handle the error !!!!
						// ...

						// if something wrong with the format then clear buffer of the socket and remove all temporary data,
						// i.e. just ignore all that we have at the moment and we'll wait new messages
						AccidentallyClearBuffer();
						return;
					}
				}
			}
		}

		public void SendRequest(string message)
		{
			if (this.Connected)
			{
				mWebSocketClient.SendRequest(message);
			}
		}
		
		public override bool Connected
		{
			get
			{
				return mConnected && WebSocketConnected;
			}
		}
		
		protected bool WebSocketConnected
		{
			get
			{
				return (mWebSocketClient != null && mWebSocketClient.Connected);
			}
		}
	}

}