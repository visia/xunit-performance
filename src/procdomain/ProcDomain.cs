﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ProcessDomain
{
    public class ProcDomain
    {
        private static ProcDomain s_currentDomain;
        private static readonly object s_currentDomainLock = new object();

        private string _domName = "DefaultProcDomain";
        private string _pipeName;
        private PipeStream _pipe;
        private Dictionary<Guid, TaskCompletionSource<CrossDomainInvokeResponse>> _pendingActions = new Dictionary<Guid, TaskCompletionSource<CrossDomainInvokeResponse>>();
        private Process _process;

        public event Action<ProcDomain> Unloading;
        public event Action<ProcDomain> Unloaded;

        public string Name { get { return _domName; } }

        public static void HostDomain(string pipeName)
        {
            ProcDomain.InitializeCurrentDomain(pipeName);

            ManualResetEventSlim unloaded = new ManualResetEventSlim(false);

            ProcDomain.GetCurrentProcDomain().Unloaded += p => unloaded.Set();

            unloaded.Wait();
        }

        public static ProcDomain CreateDomain(string name, string executablePath, bool runElevated)
        {
            ProcDomain domain = new ProcDomain();

            domain._pipeName = name + "_" + Guid.NewGuid().ToString();

            //create a named pipe
            //the child process will connect as a client but both sides act as a server and a client sending and waiting for messages
            var pipeServer = new NamedPipeServerStream(domain._pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous) { ReadMode = PipeTransmissionMode.Message };

            domain._pipe = pipeServer;

            //start waiting for a connection before creating the process
            Task clientConnected = pipeServer.WaitForConnectionAsync(); // new CancellationTokenSource(2000).Token);

            //create the process
            var proc = domain.CreateDomainProcess(executablePath, runElevated);

            //this will unwind any exception comming from WaitForConnection
            clientConnected.GetAwaiter().GetResult();

            //start listening for communications from the client
            Task throwaway = domain.ListenToChildDomainAsync();


            return domain;
        }

        public static ProcDomain CreateDomain(string name, Type remotableType, bool runElevated)
        {
            return CreateDomain(name, remotableType.Assembly.Location, runElevated);
        }

        public static ProcDomain GetCurrentProcDomain()
        {
            if (s_currentDomain == null)
            {
                InitializeCurrentDomain();
            }

            return s_currentDomain;
        }

        internal static void InitializeCurrentDomain(string pipeName = null)
        {
            lock (s_currentDomainLock)
            {
                if (s_currentDomain == null)
                {
                    s_currentDomain = new ProcDomain();

                    s_currentDomain._pipeName = pipeName;

                    //if the pipe name is specified this is a child domain
                    if (s_currentDomain._pipeName != null)
                    {
                        s_currentDomain.InitializeChildDomain();
                    }
                }
            }
        }

        private void Unload()
        {
            if (this.Unloading != null)
            {
                this.Unloading(this);
            }

            if (this.Unloaded != null)
            {
                this.Unloaded(this);
            }
        }

        private void InitializeChildDomain()
        {
            //get the first _ in the pipename as this splits the parent proc handle and the domain name
            int splitIdx = _pipeName.IndexOf('_');

            _domName = _pipeName.Substring(0, splitIdx);

            //create a client for the named pipe of the parent
            //the child process will connect as a client but both sides act as a server and a client sending and waiting for messages
            var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            _pipe = pipeClient;

            pipeClient.Connect();

            var throwaway = ListenToParentDomainAsync();
        }

        private async Task ListenToParentDomainAsync()
        {
            CrossDomainInvokeRequest parentMessage = null;

            //while the we continue to get messages from the parent process
            while ((parentMessage = await ReadNextRequestAsync()) != null)
            {
                Task throwaway = HandleInvokeRequest(parentMessage);
            }

            this.Unload();
        }

        private async Task HandleInvokeRequest(CrossDomainInvokeRequest request)
        {
            //TODO: assembly loading goo?

            var response = new CrossDomainInvokeResponse() { MessageId = request.MessageId };

            try
            {
                response.Result = await Task.Run<object>(() => request.Method.Invoke(null, request.Arguments));
            }
            catch (TargetInvocationException e)
            {
                response.Exception = e.InnerException;
            }
            catch (Exception e)
            {
                response.Exception = e;
            }

            await SendResponseAsync(response);
        }

        public Task<TResult> ExecuteAsync<TResult>(Expression<Func<TResult>> methodCall)
        {
            return ExecuteAsync<TResult>((LambdaExpression)methodCall);
        }

        public Task ExecuteAsync(Expression<Action> methodCall)
        {
            return ExecuteAsync<object>(methodCall);
        }

        private async Task<TResult> ExecuteAsync<TResult>(LambdaExpression methodCall)
        {
            var body = (MethodCallExpression)methodCall.Body;
            var methodInfo = body.Method;
            var args = body.Arguments.Select(arg => Expression.Lambda(arg).Compile().DynamicInvoke()).ToArray();

            if (methodInfo.IsAbstract || methodInfo.IsPrivate || !methodInfo.IsStatic || methodInfo.DeclaringType.IsNotPublic)
            {
                throw new ArgumentException();
            }

            if (methodInfo.GetCustomAttribute<ProcDomainExportAttribute>(inherit: false) == null)
            {
                throw new ArgumentException();
            }

            var request = new CrossDomainInvokeRequest() { Method = methodInfo, MessageId = Guid.NewGuid(), Arguments = args };

            var response = await SendRequestAndWaitAsync(request);

            if (response.Exception != null)
            {
                throw response.Exception;
            }

            return (TResult)response.Result;
        }

        private async Task SendResponseAsync(CrossDomainInvokeResponse message)
        {
            byte[] buff = message.ToByteArray();

            await _pipe.WriteAsync(buff, 0, buff.Length);
        }

        private async Task<CrossDomainInvokeResponse> SendRequestAndWaitAsync(CrossDomainInvokeRequest message)
        {
            var taskCompletionSource = new TaskCompletionSource<CrossDomainInvokeResponse>();

            //add the transation to the pendingActions before sending the message to avoid a race with completion of the action
            _pendingActions.Add(message.MessageId, taskCompletionSource);

            byte[] buff = message.ToByteArray();

            await _pipe.WriteAsync(buff, 0, buff.Length);

            //wait for the response the completion source will be triggered in the ListenForResponsesAsync loop once the 
            //appropriate message has been recieved, (matching messageId)
            return await taskCompletionSource.Task;
        }

        private async Task ListenToChildDomainAsync()
        {
            CrossDomainInvokeResponse childMessage = null;

            //while the child process is alive and we continue to get messages
            while (!_process.HasExited && (childMessage = await ReadNextResponseAsync()) != null)
            {
                TaskCompletionSource<CrossDomainInvokeResponse> completionSource;

                //find the task completion which signals the completion of the invoke request
                if (_pendingActions.TryGetValue(childMessage.MessageId, out completionSource))
                {
                    //try to mark the task as complete returning the response
                    completionSource.TrySetResult(childMessage);
                }
            }
        }

        private async Task<CrossDomainInvokeResponse> ReadNextResponseAsync()
        {
            byte[] buff = new byte[1024];

            MemoryStream memStream = new MemoryStream();

            do
            {
                int bytesRead = await _pipe.ReadAsync(buff, 0, 1024);
                if (bytesRead == 0)
                    return null;
                memStream.Write(buff, 0, bytesRead);
            } while (!_pipe.IsMessageComplete);

            return CrossDomainInvokeResponse.FromByteArray(memStream.ToArray());
        }

        private async Task<CrossDomainInvokeRequest> ReadNextRequestAsync()
        {
            _pipe.ReadMode = PipeTransmissionMode.Message;
            byte[] buff = new byte[1024];

            MemoryStream memStream = new MemoryStream();

            do
            {
                int bytesRead = await _pipe.ReadAsync(buff, 0, 1024);
                if (bytesRead == 0)
                    return null;
                memStream.Write(buff, 0, bytesRead);
            } while (!_pipe.IsMessageComplete);

            return CrossDomainInvokeRequest.FromByteArray(memStream.ToArray());
        }

        private Process CreateDomainProcess(string executablePath, bool runElevated)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();

            startInfo.UseShellExecute = true;
            startInfo.FileName = executablePath;
            startInfo.Arguments = _pipeName;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            if (runElevated)
                startInfo.Verb = "runas";

            var proc = _process = Process.Start(startInfo);

            return proc;
        }
    }
}
