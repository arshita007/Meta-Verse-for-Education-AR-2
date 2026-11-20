using System;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VipAr
{
    /// <summary>Saves camera images to the headset as JPEG files, without stalling rendering.</summary>
    public static class FrameCapture
    {
        /// <summary>
        /// Root folder for training photos. On a Quest this is
        /// /sdcard/Android/data/com.vipar.objecthighlighter/files/captures, readable with adb pull.
        /// </summary>
        public static string Root => Path.Combine(Application.persistentDataPath, "captures");

        public static void SaveJpg(Texture texture, string path, Action<bool> done = null)
        {
            int width = texture.width;
            int height = texture.height;
            AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    done?.Invoke(false);
                    return;
                }

                try
                {
                    // The camera texture holds sRGB values, which is what JPEG expects.
                    using var jpg = ImageConversion.EncodeNativeArrayToJPG(request.GetData<byte>(), GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, 92);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, jpg.ToArray());
                    done?.Invoke(true);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[VIPAR] Could not save {path}: {exception.Message}");
                    done?.Invoke(false);
                }
            });
        }
    }
}
