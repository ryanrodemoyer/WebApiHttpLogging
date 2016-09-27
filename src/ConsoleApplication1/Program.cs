using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Owin;
using Microsoft.Owin.Hosting;
using Owin;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            const string address = "http://localhost:12345";

            using (WebApp.Start<Startup>(address))
            {
                Console.WriteLine("Listening requests on {0}.", address);
                Console.WriteLine("Please, press ENTER to shutdown.");
                Console.ReadLine();
            }
        }
    }

    public sealed class Startup
    {
        private readonly HttpConfiguration _configuration = new HttpConfiguration();

        public void Configuration(IAppBuilder application)
        {
            HttpLoggingOptions options = new HttpLoggingOptions();
            options.MaxRequestLength = 64*1024;
            options.MaxResponseLength = 64*1024;
            options.WhatToDo = entry =>
            {
                Console.WriteLine("\nTracking Id: " + entry.RequestId);
                Console.WriteLine("REQUEST");
                Console.WriteLine("Verb: {0}", entry.Verb);
                Console.WriteLine("RequestUri: {0}", entry.RequestUri);
                Console.WriteLine("Request: {0}", entry.Request);
                Console.WriteLine("RequestLength: {0}", entry.RequestLength);

                Console.WriteLine("\nRESPONSE");
                Console.WriteLine("StatusCode: {0}", entry.StatusCode);
                Console.WriteLine("ReasonPhrase: {0}", entry.ReasonPhrase);
                Console.WriteLine("Response: {0}", entry.Response);
                Console.WriteLine("Content-Length: {0}", entry.ResponseLength);
            };

            application.Use<HttpLoggingMiddleware>(options);

            application.UseWebApi(_configuration);

            _configuration.MapHttpAttributeRoutes();
        }
    }

    public class HttpEntry
    {
        public Guid RequestId { get; set; }
        public string Verb { get; set; }
        public Uri RequestUri { get; set; }
        public long RequestLength { get; set; }
        public string Request { get; set; }
        public long ResponseLength { get; set; }
        public string Response { get; set; }
        public IDictionary<string, string[]> RequestHeaders { get; set; }
        public int StatusCode { get; set; }
        public string ReasonPhrase { get; set; }
        public IDictionary<string, string[]> ResponseHeaders { get; set; }

        public HttpEntry()
        {
            RequestId = Guid.NewGuid();
        }
    }

    public class HttpLoggingOptions
    {
        public string TrackingHeaderName { get; set; }

        public long? MaxRequestLength { get; set; }
        public long? MaxResponseLength { get; set; }

        public Action<HttpEntry> WhatToDo { get; set; }
    }

    public class HttpLoggingMiddleware : OwinMiddleware
    {
        private readonly long _maxRequestLength = long.MaxValue;
        private readonly long _maxResponseLength = long.MaxValue;

        private readonly string _trackingHeaderName = "http-tracking-id";

        private Action<HttpEntry> _whatToDo = null;

        public HttpLoggingMiddleware(OwinMiddleware next, HttpLoggingOptions options) : base(next)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if (!string.IsNullOrEmpty(options.TrackingHeaderName))
            {
                _trackingHeaderName = options.TrackingHeaderName;
            }

            if (options.MaxRequestLength != null)
            {
                _maxRequestLength = options.MaxRequestLength.Value;
            }
            if (options.MaxResponseLength != null)
            {
                _maxResponseLength = options.MaxResponseLength.Value;
            }

            if (options.WhatToDo != null)
            {
                _whatToDo = options.WhatToDo;
            }
        }

        public override async Task Invoke(IOwinContext context)
        {
            ContentStream requestStream = new ContentStream(new MemoryStream(), context.Request.Body);
            context.Request.Body = requestStream;

            ContentStream responseStream = new ContentStream(new MemoryStream(), context.Response.Body);
            context.Response.Body = responseStream;

            HttpEntry entry = new HttpEntry();

            Action<object> callback = o =>
            {
                IOwinContext ctx = (IOwinContext) o;

                ctx.Response.Headers.Add(_trackingHeaderName, new[]
                {
                    entry.RequestId.ToString()
                });
            };

            context.Response.OnSendingHeaders(callback, context);

            await Next.Invoke(context);
            
            entry.Verb = context.Request.Method;
            entry.RequestUri = context.Request.Uri;
            entry.RequestHeaders = context.Request.Headers;
            entry.RequestLength = requestStream.ContentLength;
            entry.Request = await GetContentAsync(requestStream, context.Request.Headers, _maxRequestLength);

            entry.StatusCode = context.Response.StatusCode;
            entry.ReasonPhrase = context.Response.ReasonPhrase;
            entry.ResponseHeaders = context.Response.Headers;
            entry.ResponseLength = responseStream.ContentLength;
            entry.Response = await GetContentAsync(responseStream, context.Response.Headers, _maxResponseLength);

            if (_whatToDo != null)
            {
                _whatToDo(entry);
            }
        }

        private static async Task<string> GetContentAsync(ContentStream stream, IDictionary<string, string[]> headers, long maxLength)
        {
            const string contentType = "Content-Type";

            string[] value;
            if (headers.TryGetValue(contentType, out value))
            {
                if (value != null && value.Length > 0)
                {
                    return await stream.ReadContentAsync(value[0], maxLength);
                }
            }

            return null;
        }
    }

    public class CustomColor
    {
        public string Name { get; set; }

        public int Red { get; set; }
        public int Green { get; set; }
        public int Blue { get; set; }

        public CustomColor() { }

        public CustomColor(string name)
        {
            Name = name;
        }

        public CustomColor(string name, int red, int green, int blue)
        {
            Name = name;
            Red = red;
            Green = green;
            Blue = blue;
        }
    }

    public class CustomController : ApiController
    {
        private static Dictionary<string, CustomColor> _colors = new Dictionary<string, CustomColor>(StringComparer.OrdinalIgnoreCase)
        {
            {"red", new CustomColor("red", 255, 0, 0)},
            {"green", new CustomColor("green", 0, 255, 0)},
            {"blue", new CustomColor("blue", 0, 0, 255)},
        };

        [HttpGet]
        [Route("api/colors")]
        public IHttpActionResult GetColors()
        {
            return Ok(_colors.Values);
        }

        [HttpGet]
        [Route("api/color")]
        public IHttpActionResult GetColor(string color)
        {
            CustomColor c;
            if (_colors.TryGetValue(color, out c))
            {
                return Ok(c);
            }

            return NotFound();
        }

        [HttpPost]
        [Route("api/colors")]
        public IHttpActionResult PostColor([FromBody] CustomColor color)
        {
            if (_colors.ContainsKey(color.Name))
            {
                return ResponseMessage(new HttpResponseMessage(HttpStatusCode.Found));
            }

            _colors[color.Name] = color;

            return Created("/api/color/" + color.Name, color);
        }

        [HttpPut]
        [Route("api/colors")]
        public IHttpActionResult PutColor([FromBody] CustomColor color)
        {
            if (_colors.ContainsKey(color.Name))
            {
                _colors[color.Name] = color;

                return Ok(color);
            }

            return NotFound();
        }
    }

    /// <summary>
    /// A class to wrap a stream for interception purposes and recording the number of bytes written to or read from the wrapped stream.
    /// </summary>
    public class ContentStream : Stream
    {
        private readonly Stream _buffer;
        private readonly Stream _stream;
        private long _contentLength;

        /// <summary>
        /// Initialize a new instance of the <see cref="ContentStream"/> class.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="stream"></param>
        public ContentStream(Stream buffer, Stream stream)
        {
            _buffer = buffer;
            _stream = stream;
        }

        /// <summary>
        /// Reads the content of the stream as a string.
        /// If the contentType is not specified (null) or does not refer to a string, this function returns the content type followed by the number of bytes in the response.
        /// If the contentType is one of the following values, the resulting content is decoded as a string and truncated to the maximum count specified.
        /// </summary>
        /// <param name="contentType">HTTP header content type.</param>
        /// <param name="maxCount">Max number of bytes returned from the stream.</param>
        /// <returns></returns>
        public async Task<string> ReadContentAsync(string contentType, long maxCount)
        {
            if (!IsTextContentType(contentType))
            {
                contentType = string.IsNullOrEmpty(contentType) ? "N/A" : contentType;
                return string.Format("{0} [{1} bytes]", contentType, ContentLength);
            }

            _buffer.Seek(0, SeekOrigin.Begin);

            long length = Math.Min(ContentLength, maxCount);

            byte[] buffer = new byte[length];
            int count = await _buffer.ReadAsync(buffer, 0, buffer.Length);

            return GetEncoding(contentType).GetString(buffer, 0, count);
        }

        private void WriteContent(byte[] buffer, int offset, int count)
        {
            _buffer.Write(buffer, offset, count);
        }

        public virtual long ContentLength => _contentLength;

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _stream.Length;

        public override long Position
        {
            get { return _stream.Position; }
            set { _stream.Position = value; }
        }

        public override void Flush()
        {
            _stream.Flush();
        }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            count = _stream.Read(buffer, offset, count);

            _contentLength += count;

            if (count != 0)
            {
                WriteContent(buffer, offset, count);
            }

            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteContent(buffer, 0, count);

            _stream.Write(buffer, offset, count);

            _contentLength += count;
        }

        protected override void Dispose(bool disposing)
        {
            _buffer.Dispose();
        }

        private static bool IsTextContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                return false;
            }

            return
                contentType.StartsWith("application/json") ||
                contentType.StartsWith("application/xml") ||
                contentType.StartsWith("text/");
        }

        private static Encoding GetEncoding(string contentType)
        {
            string charset = "utf-8";
            Regex regex = new Regex(@";\s*charset=(?<charset>[^\s;]+)");
            Match match = regex.Match(contentType);
            if (match.Success)
            {
                charset = match.Groups["charset"].Value;
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }
    }
}
