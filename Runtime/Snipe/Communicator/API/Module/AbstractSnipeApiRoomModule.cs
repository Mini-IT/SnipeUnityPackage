using System;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiRoomModule : SnipeApiModule
	{
		public delegate void RoomIdHandler(int roomID);
		public delegate void ErrorCodeHandler(string errorCode);
		public delegate void ErrorCodeUserIdHandler(string errorCode, int userID);

		/// <summary>
		/// Raised when a new user joins the room.
		/// </summary>
		/// <param name="roomID">ID of the room we joined</param>
		public event RoomIdHandler Joined;

		/// <summary>
		/// room.left
		/// <para />  This is a server notification about a client leaving the room.
		/// </summary>
		/// <param name="errorCode">Operation error code.</param>
		/// <param name="userID">ID of the user, who left the room</param>
		public event ErrorCodeUserIdHandler Left;

		/// <summary>
		/// room.dead
		/// <para />  This is a server notification about room removal that is sent to all clients in that room.
		/// </summary>
		/// <param name="errorCode">Operation error code.</param>
		public event ErrorCodeHandler Dead;

		/// <summary>
		/// Room broadcast received
		/// </summary>
		/// <param name="message">the message sent to all users in the room</param>
		public event Action<string> BroadcastReceived;

		protected AbstractSnipeApiRoomModule(AbstractSnipeApiService snipeApiService)
			: base(snipeApiService)
		{
			SubscribeOnMessageReceived(OnMessageReceived);
		}

		///<summary>
		/// room.join
		/// <para /> Adds this user to the specified room of a given game type.
		/// <br /> If room join action exists, it will be called and its results will be returned to the client.
		/// <para />  Note: A user can only be in one room.
		/// <para />  Output:
		/// <br />  * `errorCode` - `String`. Operation error code.
		/// <br />  * Additional parameters returned by room join action.
		/// <para />  Error codes:
		/// <br />  * `ok` - Operation successful.
		/// <br />  * `roomNotFound` - Room with this ID was not found.
		/// <br />  * `joinNotAllowed` - This user is not allowed to join this room.
		/// <br />  * `alreadyInRoom` - This user is already in another room.
		/// <br />  * Error codes defined by room join action.
		///</summary>
		/// <param name="typeID">Game type ID</param>
		/// <param name="roomID">Room ID</param>
		public bool Join(string typeID, int roomID, AbstractCommunicatorRequest.ResponseHandler callback = null)
		{
			var requestData = new Dictionary<string, object>()
			{
				["typeID"] = typeID,
				["roomID"] = roomID,
			};
			var request = CreateRequest("room.join", requestData);

			if (request == null)
			{
				return false;
			}

			request.Request(callback);
			return true;
		}

		/// <summary>
		/// room.leave
		/// <para /> Removes this user from the room he is currently in and then immediately disconnects the client.
		/// <para /> All room clients will receive "room.left" message with userID set.
		/// <para /> Client state on game server will be reset, room attached user lock cleared. Will call the "room.leave" script if it exists.
		/// <para /> Same sequence of events will happen on normal client disconnect.
		/// </summary>
		public bool Leave(AbstractCommunicatorRequest.ResponseHandler callback = null)
		{
			var request = CreateRequest("room.leave");

			if (request == null)
			{
				return false;
			}

			request.Request(callback);
			return true;
		}

		/// <summary>
		/// room.broadcast
		/// <para />  Broadcasts a message to all users in the room that this client is in.
		/// <para />  Will not send a standard response to the client that sent the broadcast in case of success.
		/// <para />  Upon successful broadcast all clients in the room will receive the message with the type "room.broadcast" and message contents in "msg" field.
		/// The <see cref="BroadcastReceived"/> event will be raised.
		/// <para />  Output:
		/// <br />  * `errorCode` - `String`. Operation error code.
		/// <br />  * `msg` - `String`. Message contents.
		/// <para />  Error codes:
		/// <br />  * `notInRoom` - This client has not joined a room.
		/// <br />  * `roomDead` - This room is dead.
		/// </summary>
		/// <param name="msg">Message contents.</param>
		/// <param name="callback"></param>
		public bool Broadcast(string msg, AbstractCommunicatorRequest.ResponseHandler callback = null)
		{
			var requestData = new Dictionary<string, object>()
			{
				["msg"] = msg,
			};
			var request = CreateRequest("room.broadcast", requestData);

			if (request == null)
			{
				return false;
			}

			request.Request(callback);
			return true;
		}

		/// <summary>
		/// room.event
		/// <para />  Sends a client event to the room event handler that this client is currently in.
		/// <para />  Error codes:
		/// <br />  * `notInRoom` - This client has not joined a room.
		/// <br />  * `noSuchAction` - This game type does not have an event action with this string ID.
		/// <br />  * `roomDead` - This room is dead.
		/// <br />  * Event handler custom error codes.
		/// </summary>
		/// <param name="actionID">Event action string ID</param>
		/// <param name="callback">Callback with <c>errorCode</c></param>
		/// <returns><c>true</c> if the request is sent</returns>
		public bool Event(string actionID, ErrorCodeHandler callback = null)
		{
			return Event(actionID, null, callback);
		}

		/// <summary>
		/// room.event
		/// <para />  Sends a client event to the room event handler that this client is currently in.
		/// <para />  Error codes:
		/// <br />  * `notInRoom` - This client has not joined a room.
		/// <br />  * `noSuchAction` - This game type does not have an event action with this string ID.
		/// <br />  * `roomDead` - This room is dead.
		/// <br />  * Event handler custom error codes.
		/// </summary>
		/// <param name="actionID">Event action string ID</param>
		/// <param name="actionParams">Parameters passed to the action script</param>
		/// <param name="callback"></param>
		/// <returns><c>true</c> if the request is sent</returns>
		public bool Event(string actionID, IDictionary<string, object> actionParams, ErrorCodeHandler callback = null)
		{
			var requestData = (actionParams != null)
				? new Dictionary<string, object>(actionParams)
				: new Dictionary<string, object>();
			requestData["actionID"] = actionID;

			var request = CreateRequest("room.event", requestData);

			if (request == null)
				return false;

			request.Request((errorCode, _) =>
			{
				callback?.Invoke(errorCode);
			});
			return true;
		}

		private void OnMessageReceived(string messageType, string errorCode, IDictionary<string, object> data, int requestId)
		{
			switch (messageType)
			{
				case "room.join":
					if (errorCode == SnipeErrorCodes.OK)
					{
						int roomId = data.SafeGetValue<int>("roomID");
						if (roomId != 0)
						{
							Joined?.Invoke(roomId);
						}
					}
					break;

				case "room.broadcast":
					if (errorCode == SnipeErrorCodes.OK)
					{
						string msg = data.SafeGetString("msg");
						if (!string.IsNullOrEmpty(msg))
						{
							BroadcastReceived?.Invoke(msg);
						}
					}
					break;

				case "room.left":
					Left?.Invoke(errorCode, data.SafeGetValue<int>("userID"));
					break;

				case "room.dead":
					Dead?.Invoke(errorCode);
					break;
			}
		}
	}
}
