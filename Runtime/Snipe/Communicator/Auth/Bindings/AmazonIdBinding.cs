using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AmazonIdBinding : AuthBinding<AmazonIdFetcher>
	{
		public AmazonIdBinding() : base("amzn")
		{
		}

		public void SetUserId(string uid)
		{
			if (mFetcher is AmazonIdFetcher fetcher)
			{
				fetcher.SetValue(uid);
			}
		}
	}
}