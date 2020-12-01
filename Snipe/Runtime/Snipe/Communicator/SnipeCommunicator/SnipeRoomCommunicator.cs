﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeRoomCommunicator : SnipeCommunicator
	{
		public event Action RoomJoined;

		protected SnipeCommunicator mGameCommunicator;

		protected string mRoomType;
		protected int mRoomId;

		protected string mWebSocketUrl;

		public static T Create<T>(string room_type, int room_id, SnipeCommunicator game_communicator, string websocket_url) where T : SnipeRoomCommunicator
		{
			var communicator = new GameObject("SnipeRoomCommunicator").AddComponent<T>();
			communicator.mRoomType = room_type;
			communicator.mRoomId = room_id;
			communicator.mWebSocketUrl = websocket_url;
			communicator.SetGameCommunicator(game_communicator);
			return communicator;
		}

		public SnipeRoomCommunicator() : base()
		{
			RestoreConnectionAttempts = 0;
		}

		protected override void InitClient()
		{
			InitClient(mWebSocketUrl);
		}

		private void SetGameCommunicator(SnipeCommunicator communicator)
		{
			if (mGameCommunicator != communicator)
			{
				if (mGameCommunicator != null)
				{
					mGameCommunicator.LoginSucceeded -= OnGameLogin;
				}

				mGameCommunicator = communicator;
				mGameCommunicator.LoginSucceeded += OnGameLogin;
			}
		}
		
		protected override void OnConnectionFailed()
		{
			DebugLogger.Log($"[SnipeRoomCommunicator] Game Connection failed");
			//base.OnConnectionFailed(data);

			//if (RestoreConnectionAttempts < 1 && !mDisconnecting)
			//{
			//	if (mGameCommunicator != null && mGameCommunicator.Connected)
			//	{
			//		mGameCommunicator.OnRoomConnectionFailed(data);
			//	}
			//}
		}

		private void OnGameLogin()
		{
			InitClient();
		}

		public override void Dispose()
		{
			if (mGameCommunicator != null)
			{
				mGameCommunicator.LoginSucceeded -= OnGameLogin;
				mGameCommunicator = null;
			}
			base.Dispose();
		}

		protected override void ProcessSnipeMessage(string message_type, string error_code, ExpandoObject data)
		{
			base.ProcessSnipeMessage(message_type, error_code, data);

			DebugLogger.Log("[SnipeRoomCommunicator] OnRoomResponse: " + (data != null ? data.ToJSONString() : "null"));

			//string message_type = data.SafeGetString("type");
			//string error_code = data.SafeGetString("errorCode");

			switch (message_type)
			{
				case "user.login":
					if (error_code == "ok")
					{
						// join room
						ExpandoObject request_data = new ExpandoObject();
						request_data["typeID"] = mRoomType;
						request_data["roomID"] = mRoomId;
						//Request("room.join", request_data);
					}
					//else if (data.errorCode == "userAlreadyLogin")
					break;

				case "room.join":
					OnRoomJoin(error_code, data);
					break;

				case "room.leave":
				case "user.logout":
					OnRoomLeave(data);
					break;

				case "room.left":
					OnRoomLeft(data);
					break;

				case "room.dead":
					OnRoomDead(data);
					break;

				case "room.broadcast":
					ExpandoObject broadcast_msg = null;
					if (error_code == "ok" && data.ContainsKey("msg"))
					{
						try
						{
							broadcast_msg = ExpandoObject.FromJSONString(data["msg"] as string);
						}
						catch (Exception)
						{
							//
						}
					}
					OnRoomBroadcast(error_code, broadcast_msg);
					break;
			}
		}

		protected virtual void OnRoomJoin(string error_code, ExpandoObject data)
		{
			if (RoomJoined != null && error_code == "ok")
				RoomJoined.Invoke();
		}

		protected virtual void OnRoomLeave(ExpandoObject data)
		{
			Dispose();
		}

		protected virtual void OnRoomLeft(ExpandoObject data)
		{
			
		}

		protected virtual void OnRoomDead(ExpandoObject data)
		{
			// NOTE: kit/room.dead is dispatched on any room death even when it is correctly finilized

			Dispose();
		}

		
		protected virtual void OnRoomBroadcast(string error_code, ExpandoObject msg)
		{

		}
		
		#region Analytics
		
		protected override void AnalyticsTrackConnectionSucceeded()
		{
			Analytics.TrackEvent(Analytics.EVENT_ROOM_COMMUNICATOR_CONNECTED, new ExpandoObject()
			{
				["connection_type"] = "websocket",
				["websocket_url"] = mWebSocketUrl,
			});
		}
		
		protected override void AnalyticsTrackConnectionFailed()
		{
			Analytics.TrackEvent(Analytics.EVENT_ROOM_COMMUNICATOR_DISCONNECTED, new ExpandoObject()
			{
				["communicator"] = this.name,
				//["connection_id"] = Client?.ConnectionId,
				//["disconnect_reason"] = Client?.DisconnectReason,
			});
		}
		
		#endregion Analytics
	}
}