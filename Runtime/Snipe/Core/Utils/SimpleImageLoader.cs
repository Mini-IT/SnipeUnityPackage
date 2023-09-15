using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Utils
{
	public class SimpleImageLoaderComponent : MonoBehaviour
	{
		internal void Dispose()
		{
			StopAllCoroutines();
			Destroy(this);
		}
	}
	
	public class SimpleImageLoader
	{
		private static GameObject _gameObject;
		private SimpleImageLoaderComponent _component;
		
		private static Dictionary<string, Texture2D> _cache;
		private static Dictionary<string, Sprite> _spritesCache;
		private static Dictionary<string, SimpleImageLoader> _activeLoaders;

		private const int MAX_LOADERS_COUNT = 3;
		private static int _loadersCount = 0;
		
		private static readonly Vector2 SPRITE_PIVOT = new Vector2(0.5f, 0.5f);

		public string Url { get; private set; }
		
		private bool _useCache = false;

		private Action<Texture2D> _callback;
		private List<SimpleImageLoader> _parasiteLoaders;
		
		private SimpleImageLoader()
		{
		}

		public static SimpleImageLoader Load(string url, Action<Texture2D> callback = null, bool cache = false)
		{
			if (string.IsNullOrWhiteSpace(url))
				return null;

			if (cache)
			{
				if (_cache != null)
				{
					if (_cache.TryGetValue(url, out Texture2D texture))
					{
						callback?.Invoke(texture);
						return null;
					}
				}
			}
			
			var loader = new SimpleImageLoader();
			loader._useCache = cache;

			if (_activeLoaders != null && _activeLoaders.TryGetValue(url, out var master_loader) && master_loader != null)
			{
				loader.Url = url;
				loader._callback = callback;
				
				if (master_loader._parasiteLoaders == null)
					master_loader._parasiteLoaders = new List<SimpleImageLoader>();
				master_loader._parasiteLoaders.Add(loader);
			}
			else
			{
				loader.DoLoad(url, callback);
			}
			return loader;
		}
		
		public static SimpleImageLoader LoadSprite(string url, Action<Sprite> callback = null, bool cache = false)
		{
			Sprite sprite = null;
			
			if (cache && _spritesCache != null && _spritesCache.TryGetValue(url, out sprite))
			{
				callback?.Invoke(sprite);
				return null;
			}
			
			return Load(url,
				(texture) =>
				{
					if (!cache || _spritesCache == null || !_spritesCache.TryGetValue(url, out sprite))
					{
						sprite = Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), SPRITE_PIVOT);
					}
					
					if (cache)
					{
						if (_spritesCache == null)
							_spritesCache = new Dictionary<string, Sprite>();
						_spritesCache[url] = sprite;
					}
					
					callback?.Invoke(sprite);
				},
				true
			);
		}
		
		public static SimpleImageLoader LoadSprite(string url, Image image, bool activate = true, GameObject preloader = null, bool cache = false)
		{
			Action<Sprite> apply_sprite = (sprite) =>
			{
				if (image != null)
				{
					image.sprite = sprite;
					if (activate)
						image.enabled = true;
					if (preloader != null)
						preloader.SetActive(false);
				}
			};
			
			Action<Sprite> on_sprite_loaded = (sprite) =>
			{
				apply_sprite(sprite);
				
				if (cache)
				{
					if (_spritesCache == null)
						_spritesCache = new Dictionary<string, Sprite>();
					_spritesCache[url] = sprite;
				}
			};
			
			if (cache)
			{
				if (_spritesCache != null)
				{
					if (_spritesCache.TryGetValue(url, out Sprite sprite))
					{
						apply_sprite(sprite);
						return null;
					}
				}
			}
			
			var loader = LoadSprite(url, on_sprite_loaded);
			
			if (preloader != null)
				preloader.SetActive(loader != null);
			
			return loader;
		}

		public void Cancel()
		{
			_callback = null;

			if (!_useCache && (_parasiteLoaders == null || _parasiteLoaders.Count < 1))
			{
				Destroy();
			}
		}

		private void DoLoad(string url, Action<Texture2D> callback)
		{
			if (_activeLoaders == null)
				_activeLoaders = new Dictionary<string, SimpleImageLoader>();
			_activeLoaders[url] = this;
			
			Url = url;
			_callback = callback;
			
			var component = GetComponent();
			component.StartCoroutine(LoadCoroutine(url));
		}

		private IEnumerator LoadCoroutine(string url)
		{
			while (_loadersCount >= MAX_LOADERS_COUNT)
				yield return null;
			_loadersCount++;

			using (UnityWebRequest loader = new UnityWebRequest(url))
			{
				loader.downloadHandler = new DownloadHandlerBuffer();
				yield return loader.SendWebRequest();

				if (loader.result == UnityWebRequest.Result.Success)
				{
					Texture2D texture = new Texture2D(1, 1);
					if (texture.LoadImage(loader.downloadHandler.data))
					{
						if (_useCache)
						{
							_cache ??= new Dictionary<string, Texture2D>();
							_cache[Url] = texture;
						}

						InvokeCallback(texture);
					}
					else
					{
						UnityEngine.Debug.Log($"[SimpleImageLoader] Error loading image: {url} - invalid image");
					}					
				}
				else
				{
					UnityEngine.Debug.Log($"[SimpleImageLoader] Error loading image: {url} - {loader.error}");
				}
			}

			_loadersCount--;

			Destroy();
		}
		
		private void InvokeCallback(Texture2D texture)
		{
			if (_callback != null)
			{
				_callback.Invoke(texture);
				_callback = null;
			}
			
			if (_parasiteLoaders != null)
			{
				foreach (var parasite in _parasiteLoaders)
				{
					parasite?._callback?.Invoke(texture);
				}
				_parasiteLoaders = null;
			}
		}
		
		private SimpleImageLoaderComponent GetComponent()
		{
			if (_gameObject == null)
			{
				_gameObject = new GameObject("[SimpleImageLoader]"); 
				GameObject.DontDestroyOnLoad(_gameObject);
			}
			if (_component == null)
			{
				_component = _gameObject.AddComponent<SimpleImageLoaderComponent>();
			}
			return _component;
		}
		
		private void Destroy()
		{
			_activeLoaders?.Remove(Url);
			
			if (_component != null)
			{
				_component.Dispose();
				_component = null;
			}
		}
	}
}
