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
                // 1. 优先尝试从正版 (Mojang) 获取
                string mojangUuidUrl = $"https://api.mojang.com/users/profiles/minecraft/{username}";
                HttpResponseMessage mojangResponse = await client.GetAsync(mojangUuidUrl);
                
                if (mojangResponse.IsSuccessStatusCode)
                {
                    string mojangUuidJson = await mojangResponse.Content.ReadAsStringAsync();
                    using JsonDocument mojangUuidDoc = JsonDocument.Parse(mojangUuidJson);
                    string uuid = mojangUuidDoc.RootElement.GetProperty("id").GetString() ?? "";

                    string mojangProfileUrl = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
                    string mojangProfileJson = await client.GetStringAsync(mojangProfileUrl);
                    using JsonDocument mojangProfileDoc = JsonDocument.Parse(mojangProfileJson);
                    
                    string base64Textures = "";
                    foreach (JsonElement prop in mojangProfileDoc.RootElement.GetProperty("properties").EnumerateArray())
                    {
                        if (prop.GetProperty("name").GetString() == "textures")
                        {
                            base64Textures = prop.GetProperty("value").GetString() ?? "";
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(base64Textures))
                    {
                        byte[] decodedBytes = Convert.FromBase64String(base64Textures);
                        string textureJson = Encoding.UTF8.GetString(decodedBytes);
                        using JsonDocument textureDoc = JsonDocument.Parse(textureJson);

                        if (textureDoc.RootElement.GetProperty("textures").TryGetProperty("SKIN", out JsonElement skinElement))
                        {
                            return skinElement.GetProperty("url").GetString();
                        }
                    }
                }

                // 2. 如果正版获取失败，尝试从 LittleSkin 获取
                string uuidUrl = $"{ApiRoot}/api/users/profiles/minecraft/{username}";
                HttpResponseMessage uuidResponse = await client.GetAsync(uuidUrl);
                
                if (uuidResponse.IsSuccessStatusCode)
                {
                    string uuidJson = await uuidResponse.Content.ReadAsStringAsync();
                    using JsonDocument uuidDoc = JsonDocument.Parse(uuidJson);
                    string uuid = uuidDoc.RootElement.GetProperty("id").GetString() ?? "";

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

                    if (!string.IsNullOrEmpty(base64Textures))
                    {
                        byte[] decodedBytes = Convert.FromBase64String(base64Textures);
                        string textureJson = Encoding.UTF8.GetString(decodedBytes);
                        using JsonDocument textureDoc = JsonDocument.Parse(textureJson);

                        if (textureDoc.RootElement.GetProperty("textures").TryGetProperty("SKIN", out JsonElement skinElement))
                        {
                            return skinElement.GetProperty("url").GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Skin fetch error: {ex.Message}");
                return null;
            }

            return null;
        }

        private static readonly string SteveCachePath = Path.Combine(CacheDir, "steve.png");

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
                    if (string.IsNullOrEmpty(skinUrl))
                    {
                        // 如果获取不到皮肤，检查是否有 Steve 缓存，没有就下载一个
                        if (!File.Exists(SteveCachePath))
                        {
                            try
                            {
                                byte[] steveBytes = await client.GetByteArrayAsync("https://littleskin.cn/textures/7399453957597893963"); // 一个经典的 Steve 皮肤 URL
                                await File.WriteAllBytesAsync(SteveCachePath, steveBytes);
                            }
                            catch { return null; }
                        }
                        return ExtractAvatarFromSkin(SteveCachePath);
                    }

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
                fullSkin.UriSource = new Uri(skinPath, UriKind.Absolute);
                fullSkin.CacheOption = BitmapCacheOption.OnLoad;
                fullSkin.CreateOptions = BitmapCreateOptions.None;
                fullSkin.EndInit();

                if (fullSkin.PixelWidth == 0) 
                {
                    // 如果图片还没加载完，强制同步加载（或者至少等待）
                    // 在 OnLoad 模式下，EndInit 之后应该已经加载了，但预防万一
                }

                // 提取头像区域: X=8, Y=8, Width=8, Height=8
                // 注意：Minecraft 皮肤中头部的正面位于 (8, 8) 到 (16, 16)
                CroppedBitmap cropped = new CroppedBitmap(fullSkin, new System.Windows.Int32Rect(8, 8, 8, 8));
                
                // 冻结以确保跨线程安全
                cropped.Freeze();

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
                    avatar.Freeze(); // 冻结以确保跨线程安全
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
