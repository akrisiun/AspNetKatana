// <copyright file="CanonicalRequestPatterns.cs" company="Katana contributors">
//   Copyright 2011-2012 Katana contributors
// </copyright>
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Katana.Performance.ReferenceApp
{
    using AppFunc = Func<IDictionary<string, object>, Task>;
    using SendFileFunc =
        Func<string, // File Name and path
                long, // Initial file offset
                long?, // Byte count, null for remainder of file
                CancellationToken,
                Task>; // Complete
    using WebSocketAccept =
        Action<IDictionary<string, object>, // WebSocket Accept parameters
                Func<IDictionary<string, object>, // WebSocket environment
                        Task>>; // Complete
    using WebSocketReceiveAsync =
        Func<ArraySegment<byte> /* data */,
                CancellationToken /* cancel */,
                Task<Tuple<int /* messageType */,
                                bool /* endOfMessage */,
                                int>>>; /* count */
    using WebSocketSendAsync =
        Func<ArraySegment<byte> /* data */,
                int /* messageType */,
                bool /* endOfMessage */,
                CancellationToken /* cancel */,
                Task>;

    public class CanonicalRequestPatterns
    {
        private readonly AppFunc _next;
        private readonly Dictionary<string, Tuple<AppFunc, string>> _paths;

        private readonly byte[] _2KAlphabet = Util.AlphabetCRLF(2 << 10).ToArray();

        public CanonicalRequestPatterns(AppFunc next)
        {
            _next = next;

            _paths = new Dictionary<string, Tuple<AppFunc, string>>();
            _paths["/"] = new Tuple<AppFunc, string>(Index, null);

            var items = GetType().GetMethods()
                .Select(methodInfo => new
                {
                    MethodInfo = methodInfo,
                    Attribute = methodInfo.GetCustomAttributes(true).OfType<CanonicalRequestAttribute>().SingleOrDefault()
                })
                .Where(item => item.Attribute != null)
                .Select(item => new
                {
                    App = (AppFunc)Delegate.CreateDelegate(typeof(AppFunc), this, item.MethodInfo),
                    item.Attribute.Description,
                    item.Attribute.Path,
                });

            foreach (var item in items)
            {
                _paths.Add(item.Path, Tuple.Create(item.App, item.Description));
            }
        }

        public Task Invoke(IDictionary<string, object> env)
        {
            Tuple<AppFunc, string> handler;
            return _paths.TryGetValue(Util.RequestPath(env), out handler)
                ? handler.Item1(env)
                : _next(env);
        }

        public Task Index(IDictionary<string, object> env)
        {
            Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/html" };
            Stream output = Util.ResponseBody(env);
            using (var writer = new StreamWriter(output))
            {
                writer.Write("<ul>");
                foreach (var kv in _paths.Where(item => item.Value.Item2 != null))
                {
                    writer.Write("<li><a href='");
                    writer.Write(kv.Key);
                    writer.Write("'>");
                    writer.Write(kv.Key);
                    writer.Write("</a> ");
                    writer.Write(kv.Value.Item2);
                    writer.Write("</li>");
                }
                writer.Write("</ul>");
            }
            return Task.FromResult<object>(null);
        }

        [CanonicalRequest(Path = "/small-immediate-syncwrite", Description = "Return 2kb ascii byte[] in a sync Write")]
        public Task SmallImmediateSyncWrite(IDictionary<string, object> env)
        {
            var headers = Util.ResponseHeaders(env);
            headers["Content-Type"] = new[] { "text/plain" };
            headers["Content-Length"] = new[] { "2048" };

            Util.ResponseBody(env).Write(_2KAlphabet, 0, 2048);
            return Task.FromResult<object>(null);
        }

        [CanonicalRequest(Path = "/large-immediate-syncwrite", Description = "Return 1mb ascii byte[] in 2kb sync Write")]
        public Task LargeImmediateSyncWrite(IDictionary<string, object> env)
        {
            Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
            Stream responseBody = Util.ResponseBody(env);
            for (int loop = 0; loop != (1 << 20) / (2 << 10); ++loop)
            {
                responseBody.Write(_2KAlphabet, 0, 2048);
            }
            return Task.FromResult<object>(null);
        }

        [CanonicalRequest(Path = "/large-immediate-asyncwrite", Description = "Return 1mb ascii byte[] in 2kb await WriteAsync")]
        public async Task LargeImmediateAsyncWrite(IDictionary<string, object> env)
        {
            Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
            Stream responseBody = Util.ResponseBody(env);
            for (int loop = 0; loop != (1 << 20) / (2 << 10); ++loop)
            {
                await responseBody.WriteAsync(_2KAlphabet, 0, 2048);
            }
        }

        [CanonicalRequest(Path = "/large-blockingwork-syncwrite", Description = "Return 1mb ascii byte[] in 2kb sync Write with 20ms thread sleeps every 8 writes")]
        public Task LargeBlockingWorkSyncWrite(IDictionary<string, object> env)
        {
            Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
            Stream responseBody = Util.ResponseBody(env);
            for (int loop = 0; loop != (1 << 20) / (2 << 10); ++loop)
            {
                responseBody.Write(_2KAlphabet, 0, 2048);
                if ((loop % 8) == 0)
                {
                    Thread.Sleep(20);
                }
            }
            return Task.FromResult<object>(null);
        }

        [CanonicalRequest(Path = "/large-awaitingwork-asyncwrite", Description = "Return 1mb ascii byte[] in 2kb await WriteAsync with 20ms awaits every 8 writes")]
        public async Task LargeAwaitingWorkAsyncWrite(IDictionary<string, object> env)
        {
            Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
            Stream responseBody = Util.ResponseBody(env);
            for (int loop = 0; loop != (1 << 20) / (2 << 10); ++loop)
            {
                await responseBody.WriteAsync(_2KAlphabet, 0, 2048);
                if ((loop % 8) == 0)
                {
                    await Task.Delay(20);
                }
            }
        }

        [CanonicalRequest(Path = "/small-longpolling-syncwrite", Description = "Return 2kb sync Write after 12sec await delay")]
        public async Task SmallLongPollingSyncWrite(IDictionary<string, object> env)
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
            Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
            Util.ResponseBody(env).Write(_2KAlphabet, 0, 2048);
        }

        [CanonicalRequest(Path = "/echo-websocket", Description = "Websocket accept that echoes incoming message back as outgoing")]
        public Task EchoWebsocket(IDictionary<string, object> env)
        {
            var accept = Util.Get<WebSocketAccept>(env, "websocket.Accept");
            if (accept != null)
            {
                accept(null, EchoWebsocketCallback);
            }
            else
            {
                Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
                using (var writer = new StreamWriter(Util.ResponseBody(env)))
                {
                    writer.WriteLine("This url is designed to be called with a websocket client.");
                    writer.WriteLine("It will echo incoming message data back as outgoing.");
                }
            }
            return Task.FromResult<object>(null);
        }

        private async Task EchoWebsocketCallback(IDictionary<string, object> env)
        {
            var callCancelled = Util.Get<CancellationToken>(env, "owin.CallCancelled");
            var receiveAsync = Util.Get<WebSocketReceiveAsync>(env, "websocket.ReceiveAsync");
            var sendAsync = Util.Get<WebSocketSendAsync>(env, "websocket.SendAsync");

            var buffer = new ArraySegment<byte>(new byte[2 << 10]);
            while (!callCancelled.IsCancellationRequested)
            {
                Tuple<int, bool, int> message = await receiveAsync(buffer, callCancelled);
                await sendAsync(new ArraySegment<byte>(buffer.Array, 0, message.Item3), message.Item1, message.Item2, callCancelled);
            }
        }

        [CanonicalRequest(Path = "/small-staticfile", Description = "Sending 2k static file with server accelleration extension")]
        public async Task SmallStaticFile(IDictionary<string, object> env)
        {
            var sendFileAsync = Util.Get<SendFileFunc>(env, "sendfile.SendAsync");
            if (sendFileAsync != null)
            {
                Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
                await sendFileAsync("public\\small.txt", 0, null, CancellationToken.None);
            }
            else
            {
                env["owin.ResponseStatusCode"] = 500;
                Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
                using (var writer = new StreamWriter(Util.ResponseBody(env)))
                {
                    writer.WriteLine("This server does not support sendfile.SendAsync extension.");
                }
            }
        }

        [CanonicalRequest(Path = "/large-staticfile", Description = "Sending 1m static file with server accelleration extension")]
        public async Task LargeStaticFile(IDictionary<string, object> env)
        {
            var sendFileAsync = Util.Get<SendFileFunc>(env, "sendfile.SendAsync");
            if (sendFileAsync != null)
            {
                Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
                await sendFileAsync("public\\large.txt", 0, null, CancellationToken.None);
            }
            else
            {
                env["owin.ResponseStatusCode"] = 500;
                Util.ResponseHeaders(env)["Content-Type"] = new[] { "text/plain" };
                using (var writer = new StreamWriter(Util.ResponseBody(env)))
                {
                    writer.WriteLine("This server does not support sendfile.SendAsync extension.");
                }
            }
        }
    }
}