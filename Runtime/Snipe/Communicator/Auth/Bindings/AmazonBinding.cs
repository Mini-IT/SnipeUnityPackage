using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AmazonBinding : AuthBinding<AmazonIdFetcher>
	{
		public AmazonBinding() : base("amzn")
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