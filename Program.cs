using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;
using HtmlAgilityPack;

namespace dotrule
{
    class Program
    {
        static RestClient client;

        static void SavePageAt(string decodedUrl, string filePath)
        {
            var request = new RestRequest(decodedUrl, Method.GET);
            var response = client.Execute(request);
            var content = response.Content;

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            using (var fs = System.IO.File.OpenWrite(filePath))
            {
                doc.Save(fs);
                fs.Flush();
            }
        }

        static async Task<string[]> SearchFor(string pid, string tag, params string[] additionalTags)
        {
            var requestFormatter = new System.Text.StringBuilder("index.php?page=post&s=list&tags=");
            requestFormatter.Append(tag);
            for (int i = 0; i < additionalTags.Length; ++i)
            {
                requestFormatter.Append('+');
                requestFormatter.Append(additionalTags[i]);
            }
            // Console.WriteLine("Requesting " + requestFormatter.ToString());
            var request = new RestRequest(requestFormatter.ToString(), Method.GET);
            request.AddParameter("pid", pid);
            // Console.Write(client.BuildUri(request));
            var response = await client.ExecuteTaskAsync(request);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                // TODO: exception?
                return null;
            }

            var content = response.Content;
            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            return doc.DocumentNode.Descendants("span")
            .Where(span => span.HasClass("thumb"))
            .Select(span =>
            {
                var node = span.FirstChild;
                var href = node.Attributes["href"];
                var decodedValue = HtmlEntity.DeEntitize(href.Value);
                return decodedValue;
            }).ToArray();
        }

        static async Task<string> GetDownloadUrl(string decodedUrl)
        {
            var request = new RestRequest(decodedUrl, Method.GET);
            var response = await client.ExecuteTaskAsync(request);
            var content = response.Content;
            var doc = new HtmlDocument();
            doc.LoadHtml(content);
            var nodes = doc.DocumentNode.Descendants("source")
            .Where(span => span.HasAttributes && span.Attributes["type"].Value == "video/webm")
            .Select(src => src.Attributes["src"].Value).ToArray();
            if (nodes.Length == 0) return null;
            return nodes[0];
        }

        static async Task DownloadWebmAt(string srcUrl, string fileNamePrefix = "")
        {
            if (srcUrl == null) return;
            var fileNameIdx = srcUrl.LastIndexOf('/');
            if (fileNameIdx == -1 || fileNameIdx == srcUrl.Length - 1) return;
            var fileName = fileNamePrefix + srcUrl.Substring(fileNameIdx + 1);
            var downloadClient = new RestClient(srcUrl);
            var downloadResponse = await downloadClient.ExecuteTaskAsync(new RestRequest("", Method.GET));
            System.IO.File.WriteAllBytes(fileName, downloadResponse.RawBytes);
        }

        static void InitClients()
        {
            client = new RestClient("https://rule34.xxx");
        }

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dotrule pid tag [additionalTags]");
                return;
            }

            int pid;
            int pidTo = -1;
            var toIdx = args[0].IndexOf('-');
            if (toIdx != -1)
            {
                var pid0 = args[0].Substring(0, toIdx);
                if (!Int32.TryParse(pid0, out pid))
                {
                    Console.WriteLine($"Invalid PID {args[0]}!");
                    return;
                }

                var pid1 = args[0].Substring(toIdx + 1);
                if (!Int32.TryParse(pid1, out pidTo))
                {
                    Console.WriteLine($"Invalid PID {args[0]}!");
                    return;
                }
            }
            else
            {
                if (!Int32.TryParse(args[0], out pid))
                {
                    Console.WriteLine($"Invalid PID {args[0]}!");
                    return;
                }
            }

            var tag = args[1];
            var additionalTags = args.Skip(2).ToArray();

            InitClients();

            Console.WriteLine($"Downloading {pid} with tag {tag} and {additionalTags.Length} additional tags...");
            var downloadedCount = SearchAndDownload(pid, tag, additionalTags);

            while (downloadedCount > 0 && pid + downloadedCount <= pidTo)
            {
                pid = pid + downloadedCount;

                Console.WriteLine($"\nDownloading {pid} with tag {tag} and {additionalTags.Length} additional tags...");
                downloadedCount = SearchAndDownload(pid, tag, additionalTags);
            }
        }

        static int SearchAndDownload(int pid, string tag, params string[] additionalTags)
        {
            var decodedValues = SearchFor(pid.ToString(), tag, additionalTags).Result;

            if (decodedValues == null)
            {
                Console.WriteLine("No valid match found!");
                return -1;
            }

            Console.Write($"Downloaidng 0/{decodedValues.Length}");

            int downloadIndex = 1;
            Parallel.ForEach(decodedValues, (v, state, index) =>
            {
                var rand = new Random();
                Thread.Sleep((int)(100 + rand.NextDouble() * 800));
                var downloadUrl = GetDownloadUrl(v).Result;
                DownloadWebmAt(downloadUrl, string.Format("{0:D4}.", index + pid)).Wait();
                int currentDownloadIndex = downloadIndex++;
                var sb = new System.Text.StringBuilder(decodedValues.Length);

                for (int i = 0; i < currentDownloadIndex; ++i)
                {
                    sb.Append('#');
                }
                for (int i = currentDownloadIndex; i < decodedValues.Length; ++i)
                {
                    sb.Append('_');
                }
                Console.Write($"\rDownloading [{sb.ToString()}] {currentDownloadIndex}/{decodedValues.Length}");
            });
            return decodedValues.Length;
        }
    }
}
