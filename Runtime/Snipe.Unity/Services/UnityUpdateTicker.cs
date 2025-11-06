using System;
using UnityEngine.LowLevel;

namespace MiniIT.Snipe.Unity
{
	public sealed class UnityUpdateTicker : ITicker, IDisposable
	{
		public event Action OnTick
		{
			add
			{
				lock (_lock)
				{
					_onTick += value;

					if (_listenersCount == 0)
					{
						AddLoopSystem(_loopSystem);
					}

					_listenersCount++;
				}
			}

			remove
			{
				lock (_lock)
				{
					_onTick -= value;
					_listenersCount--;

					if (_listenersCount == 0)
					{
						RemoveLoopSystem(_loopSystem);
					}
				}
			}
		}

		private Action _onTick;
		private int _listenersCount = 0;
		private readonly object _lock = new object();

		private readonly PlayerLoopSystem _loopSystem;

		public UnityUpdateTicker()
		{
			_loopSystem = new PlayerLoopSystem()
			{
				updateDelegate = OnLoopSystemUpdate,
				type = typeof(UnityUpdateTicker)
			};
		}

		public void Dispose()
		{
			lock (_lock)
			{
				RemoveLoopSystem(_loopSystem);

				_onTick = null;
				_listenersCount = 0;
			}
		}

		private void OnLoopSystemUpdate()
		{
			Action action;
			lock (_lock)
			{
				action = _onTick;
			}
			action?.Invoke();
		}

		private static void AddLoopSystem(PlayerLoopSystem loop)
		{
			PlayerLoopSystem loopSystems = PlayerLoop.GetCurrentPlayerLoop();

			for (int i = 0; i < loopSystems.subSystemList.Length; i++)
			{
				if (loopSystems.subSystemList[i].type == loop.type)
				{
					// The loop system is already in the list. Don't add it twice
					return;
				}
			}

			Array.Resize(ref loopSystems.subSystemList, loopSystems.subSystemList.Length + 1);
			loopSystems.subSystemList[^1] = loop;

			PlayerLoop.SetPlayerLoop(loopSystems);
		}

		private static void RemoveLoopSystem(PlayerLoopSystem loop)
		{
			PlayerLoopSystem loopSystems = PlayerLoop.GetCurrentPlayerLoop();

			int index = -1;

			for (int i = 0; i < loopSystems.subSystemList.Length; i++)
			{
				if (loopSystems.subSystemList[i].type == loop.type)
				{
					index = i;
					break;
				}
			}

			if (index >= 0)
			{
				var list = new PlayerLoopSystem[loopSystems.subSystemList.Length - 1];
				Array.ConstrainedCopy(loopSystems.subSystemList, 0, list, 0, index);
				Array.ConstrainedCopy(loopSystems.subSystemList, index + 1, list, index, loopSystems.subSystemList.Length - index - 1);
				loopSystems.subSystemList = list;

				PlayerLoop.SetPlayerLoop(loopSystems);
			}
		}
	}
}
