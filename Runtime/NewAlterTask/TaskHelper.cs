#if UNITY_WEBGL
#define SINGLE_THREAD
#endif

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

#if SINGLE_THREAD
using YieldAwaitable = Cysharp.Threading.Tasks.YieldAwaitable;
#else
using YieldAwaitable = System.Runtime.CompilerServices.YieldAwaitable;
#endif

namespace MiniIT.Threading
{

#if SINGLE_THREAD

	public static partial class TaskHelper
	{
		public static UniTask Run(Action action)
		{
			action.Invoke();
			return UniTask.CompletedTask;
		}

		public static UniTask<T> Run<T>(Func<T> func)
		{
			T result = func.Invoke();
			return UniTask.FromResult(result);
		}

		public static void RunAndForget(Action action, CancellationToken cancellationToken = default)
		{
			var completion = new UniTaskCompletionSource();
			Wrap(action).Forget();
			//#else
			UniTask.RunOnThreadPool(action, false, cancellationToken).Forget();
			//#endif
		}

		public static UniTask Wrap(Action action)
		{
			action.Invoke();
			return UniTask.CompletedTask;
		}

		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static UniTask Delay(TimeSpan delay)
			=> UniTask.Delay(delay);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static UniTask Delay(TimeSpan delay, CancellationToken cancellationToken)
			=> UniTask.Delay(delay, cancellationToken: cancellationToken);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static UniTask Delay(int milliseconds)
			=> UniTask.Delay(milliseconds);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static UniTask Delay(int milliseconds, CancellationToken cancellationToken)
			=> UniTask.Delay(milliseconds, cancellationToken: cancellationToken);
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static YieldAwaitable Yield() => UniTask.Yield();
	}

#else // Not SINGLE_THREAD

	public static partial class TaskHelper
	{
		public static Task Run(Action action)
		{
			return Task.Run(action);
		}

		public static Task<T> Run<T>(Func<T> func)
		{
			return Task.Run(func);
		}

		public static void RunAndForget(Action action, CancellationToken cancellationToken = default)
		{
			UniTask.RunOnThreadPool(action, false, cancellationToken).Forget();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Task Delay(TimeSpan delay)
			=> Task.Delay(delay);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
			=> Task.Delay(delay, cancellationToken: cancellationToken);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Task Delay(int milliseconds)
			=> Task.Delay(milliseconds);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Task Delay(int milliseconds, CancellationToken cancellationToken)
			=> Task.Delay(milliseconds, cancellationToken: cancellationToken);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static YieldAwaitable Yield() => Task.Yield();

		public static async UniTask Wrap(Task task)
		{
			await UniTask.WaitUntil(() => task.IsCompleted);
		}

		public static async UniTask<T> Wrap<T>(Task<T> task)
		{
			await UniTask.WaitUntil(() => task.IsCompleted);
			return task.Result;
		}
	}
#endif
}
