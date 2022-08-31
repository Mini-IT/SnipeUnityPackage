using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MiniIT;

namespace MiniIT.Snipe
{
	
	public class UnauthorizedUserAttributeGetter
	{
		public delegate void GetUserAttributeCallback(string error_code, string user_name, string key, object value);
		
		private SnipeCommunicator mCommunicator;
		
		public UnauthorizedUserAttributeGetter(SnipeCommunicator communicator)
		{
			mCommunicator = communicator;
		}

		public void GetUserAttribute(string provider_id, string user_id, string key, GetUserAttributeCallback callback)
		{
			if (mCommunicator == null)
			{
				callback?.Invoke("error", "", key, null);
				return;
			}
			
			mCommunicator.CreateRequest(SnipeMessageTypes.AUTH_ATTR_GET)?.RequestUnauthorized(
				new SnipeObject()
				{
					["provider"] = provider_id,
					["login"] = user_id,
					["key"] = key,
				},
				(error_code, response) =>
				{
					if (callback != null)
					{
						if (response != null)
						{
							callback.Invoke(error_code, response?.SafeGetString("name"), response?.SafeGetString("key"), response?["val"]);
						}
						else
						{
							callback.Invoke("error", "", key, null);
						}
					}
				});
		}
	}
}