using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MizuLauncher
{
    public class LittleSkinFetcher
    {
        private static readonly HttpClient client = new HttpClient();
        private const string ApiRoot = "https://littleskin.cn/api/yggdrasil";
        private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "skins");

        public static async Task<string?> GetSkinUrlAsync(string username)
        {
            try
            {
                // 1. 通过用户名获取 UUID
                string uuidUrl = $"{ApiRoot}/api/users/profiles/minecraft/{username}";
                HttpResponseMessage uuidResponse = await client.GetAsync(uuidUrl);
                
                if (!uuidResponse.IsSuccessStatusCode) return null;

                string uuidJson = await uuidResponse.Content.ReadAsStringAsync();
                using JsonDocument uuidDoc = JsonDocument.Parse(uuidJson);
                string uuid = uuidDoc.RootElement.GetProperty("id").GetString() ?? "";

                // 2. 通过 UUID 获取 Profile（包含材质的 Base64 字符串）
                string profileUrl = $"{ApiRoot}/sessionserver/session/minecraft/profile/{uuid}";
                string profileJson = await client.GetStringAsync(profileUrl);
                using JsonDocument profileDoc = JsonDocument.Parse(profileJson);
                
                string base64Textures = "";
                foreach (JsonElement prop in profileDoc.RootElement.GetProperty("properties").EnumerateArray())
                {
                    if (prop.GetProperty("name").GetString() == "textures")
                    {
                        base64Textures = prop.GetProperty("value").GetString() ?? "";
                        break;
                    }
                }

                if (string.IsNullOrEmpty(base64Textures)) return null;

                // 3. 解码 Base64 获取最终的皮肤 URL
                byte[] decodedBytes = Convert.FromBase64String(base64Textures);
                string textureJson = Encoding.UTF8.GetString(decodedBytes);
                using JsonDocument textureDoc = JsonDocument.Parse(textureJson);

                if (textureDoc.RootElement.GetProperty("textures").TryGetProperty("SKIN", out JsonElement skinElement))
                {
                    return skinElement.GetProperty("url").GetString();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LittleSkin fetch error: {ex.Message}");
                return null;
            }

            return null;
        }

        public static async Task<BitmapImage?> GetAvatarAsync(string username)
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                {
                    Directory.CreateDirectory(CacheDir);
                }

                string cachePath = Path.Combine(CacheDir, $"{username}.png");

                if (!File.Exists(cachePath))
                {
                    string? skinUrl = await GetSkinUrlAsync(username);
                    if (string.IsNullOrEmpty(skinUrl)) return null;

                    byte[] skinBytes = await client.GetByteArrayAsync(skinUrl);
                    await File.WriteAllBytesAsync(cachePath, skinBytes);
                }

                // 从皮肤图片中提取头像区域 (8,8,8,8)
                return ExtractAvatarFromSkin(cachePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Avatar fetch error: {ex.Message}");
                return null;
            }
        }

        private static BitmapImage? ExtractAvatarFromSkin(string skinPath)
        {
            try
            {
                BitmapImage fullSkin = new BitmapImage();
                fullSkin.BeginInit();
                fullSkin.UriSource = new Uri(skinPath);
                fullSkin.CacheOption = BitmapCacheOption.OnLoad;
                fullSkin.EndInit();

                // 提取头像区域: X=8, Y=8, Width=8, Height=8
                // 注意：Minecraft 皮肤中头部的正面位于 (8, 8) 到 (16, 16)
                CroppedBitmap cropped = new CroppedBitmap(fullSkin, new System.Windows.Int32Rect(8, 8, 8, 8));
                
                // 将 CroppedBitmap 转回 BitmapImage 方便 UI 使用
                BitmapImage avatar = new BitmapImage();
                using (MemoryStream ms = new MemoryStream())
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(cropped));
                    encoder.Save(ms);
                    ms.Seek(0, SeekOrigin.Begin);

                    avatar.BeginInit();
                    avatar.StreamSource = ms;
                    avatar.CacheOption = BitmapCacheOption.OnLoad;
                    avatar.EndInit();
                }
                return avatar;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Avatar extraction error: {ex.Message}");
                return null;
            }
        }
    }
}
