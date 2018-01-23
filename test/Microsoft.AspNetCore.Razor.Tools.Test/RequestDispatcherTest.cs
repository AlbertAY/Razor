// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CommandLine;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Razor.Tools
{
    public class RequestDispatcherTest
    {
        private static BuildRequest EmptyBuildRequest => new BuildRequest(1, Array.Empty<BuildRequest.Argument>());

        private static BuildResponse EmptyBuildResponse => new CompletedBuildResponse(
            returnCode: 0,
            utf8output: false,
            output: string.Empty);

        [Fact]
        public async Task ReadFailure()
        {
            // Arrange
            var stream = new Mock<Stream>();
            var compilerHost = CreateCompilerHost();
            var connectionHost = CreateConnectionHost();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None);
            var connection = CreateConnection(stream.Object);

            // Act
            var result = await dispatcher.AcceptConnection(
                Task.FromResult<Connection>(connection), accept: true, cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(ConnectionResult.Reason.CompilationNotStarted, result.CloseReason);
        }

        /// <summary>
        /// A failure to write the results to the client is considered a client disconnection.  Any error
        /// from when the build starts to when the write completes should be handled this way. 
        /// </summary>
        [Fact]
        public async Task WriteError()
        {
            // Arrange
            var memoryStream = new MemoryStream();
            await EmptyBuildRequest.WriteAsync(memoryStream, CancellationToken.None).ConfigureAwait(true);
            memoryStream.Position = 0;

            var stream = new Mock<Stream>(MockBehavior.Strict);
            stream
                .Setup(x => x.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((byte[] array, int start, int length, CancellationToken ct) => memoryStream.ReadAsync(array, start, length, ct));

            var connection = CreateConnection(stream.Object);
            var compilerHost = CreateCompilerHost(c =>
            {
                c.ExecuteFunc = (req, ct) =>
                {
                    return EmptyBuildResponse;
                };
            });
            var connectionHost = CreateConnectionHost();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None);

            // Act
            // We expect WriteAsync to fail because the mock stream doesn't have a corresponding setup.
            var connectionResult = await dispatcher.AcceptConnection(
                Task.FromResult<Connection>(connection), accept: true, cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(ConnectionResult.Reason.ClientDisconnect, connectionResult.CloseReason);
            Assert.Null(connectionResult.KeepAlive);
        }

        /// <summary>
        /// Ensure the Connection correctly handles the case where a client disconnects while in the 
        /// middle of a build event.
        /// </summary>
        [Fact]
        public async Task ClientDisconnectsDuringBuild()
        {
            // Arrange
            var connectionHost = Mock.Of<ConnectionHost>();

            // Fake a long running build task here that we can validate later on.
            var buildTaskSource = new TaskCompletionSource<bool>();
            var buildTaskCancellationToken = default(CancellationToken);
            var compilerHost = CreateCompilerHost(c =>
            {
                c.ExecuteFunc = (req, ct) =>
                {
                    Task.WaitAll(buildTaskSource.Task);
                    return EmptyBuildResponse;
                };
            });
            
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None);
            var readyTaskSource = new TaskCompletionSource<bool>();
            var disconnectTaskSource = new TaskCompletionSource<bool>();
            var connectionTask = CreateConnectionWithEmptyBuildRequest(c =>
            {
                c.WaitForDisconnectAsyncFunc = (ct) =>
                {
                    buildTaskCancellationToken = ct;
                    readyTaskSource.SetResult(true);
                    return disconnectTaskSource.Task;
                };
            });

            var handleTask = dispatcher.AcceptConnection(
                connectionTask, accept: true, cancellationToken: CancellationToken.None);

            // Wait until WaitForDisconnectAsync task is actually created and running.
            await readyTaskSource.Task.ConfigureAwait(false);

            // Act
            // Now simulate a disconnect by the client.
            disconnectTaskSource.SetResult(true);
            var connectionResult = await handleTask;
            buildTaskSource.SetResult(true);

            // Assert
            Assert.Equal(ConnectionResult.Reason.ClientDisconnect, connectionResult.CloseReason);
            Assert.Null(connectionResult.KeepAlive);
            Assert.True(buildTaskCancellationToken.IsCancellationRequested);
        }

        [Fact]
        public async Task AcceptFalse_RejectsBuildRequest()
        {
            // Arrange
            var stream = new TestableStream();
            await EmptyBuildRequest.WriteAsync(stream.ReadStream, CancellationToken.None);
            stream.ReadStream.Position = 0;

            var connection = CreateConnection(stream);
            var connectionHost = CreateConnectionHost();
            var compilerHost = CreateCompilerHost();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None);

            // Act
            var connectionResult = await dispatcher.AcceptConnection(
                Task.FromResult<Connection>(connection), accept: false, cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(ConnectionResult.Reason.CompilationNotStarted, connectionResult.CloseReason);
            stream.WriteStream.Position = 0;
            var response = await BuildResponse.ReadAsync(stream.WriteStream).ConfigureAwait(false);
            Assert.Equal(BuildResponse.ResponseType.Rejected, response.Type);
        }

        [Fact]
        public async Task ShutdownRequest_ReturnsShutdownResponse()
        {
            // Arrange
            var stream = new TestableStream();
            await BuildRequest.CreateShutdown().WriteAsync(stream.ReadStream, CancellationToken.None);
            stream.ReadStream.Position = 0;

            var connection = CreateConnection(stream);
            var connectionHost = CreateConnectionHost();
            var compilerHost = CreateCompilerHost();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None);

            // Act
            var connectionResult = await dispatcher.AcceptConnection(
                Task.FromResult<Connection>(connection), accept: true, cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(ConnectionResult.Reason.ClientShutdownRequest, connectionResult.CloseReason);
            stream.WriteStream.Position = 0;
            var response = await BuildResponse.ReadAsync(stream.WriteStream).ConfigureAwait(false);
            Assert.Equal(BuildResponse.ResponseType.Shutdown, response.Type);
        }

        [Fact]
        public async Task ConnectionHostThrowsWhenConnecting()
        {
            // Arrange
            var connectionHost = new Mock<ConnectionHost>(MockBehavior.Strict);
            connectionHost.Setup(c => c.WaitForConnectionAsync(It.IsAny<CancellationToken>())).Throws(new Exception());
            var compilerHost = CreateCompilerHost();
            var dispatcher = new DefaultRequestDispatcher(connectionHost.Object, compilerHost, CancellationToken.None);
            var connection = CreateConnection(Mock.Of<Stream>());

            // Act
            var connectionResult = await dispatcher.AcceptConnection(
                Task.FromResult<Connection>(connection), accept: true, cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(ConnectionResult.Reason.CompilationNotStarted, connectionResult.CloseReason);
            Assert.Null(connectionResult.KeepAlive);
        }

        [Fact]
        public async Task ConnectionThrowsWhenConnecting()
        {
            // Arrange
            var compilerHost = CreateCompilerHost();
            var connectionHost = CreateConnectionHost();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None);
            var connectionTask = Task.FromException<Connection>(new Exception());

            // Act
            var connectionResult = await dispatcher.AcceptConnection(
                connectionTask, accept: true, cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(ConnectionResult.Reason.CompilationNotStarted, connectionResult.CloseReason);
            Assert.Null(connectionResult.KeepAlive);
        }

        [Fact]
        public async Task ConnectionThrowsWhenHandlingBuild()
        {
            // Arrange
            var called = false;
            var connectionTask = CreateConnectionWithEmptyBuildRequest(c =>
            {
                c.WaitForDisconnectAsyncFunc = (ct) =>
                {
                    called = true;
                    throw new Exception();
                };
            });
            
            var compilerHost = CreateCompilerHost();
            var connectionHost = CreateConnectionHost();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None);

            // Act
            var connectionResult = await dispatcher.AcceptConnection(
                connectionTask, accept: true, cancellationToken: CancellationToken.None);

            // Assert
            Assert.True(called);
            Assert.Equal(ConnectionResult.Reason.ClientException, connectionResult.CloseReason);
            Assert.Null(connectionResult.KeepAlive);
        }

        [Fact]
        public void KeepAliveNoConnections()
        {
            // Arrange
            var keepAlive = TimeSpan.FromSeconds(3);
            var compilerHost = CreateCompilerHost();
            var connectionHost = new Mock<ConnectionHost>();
            connectionHost
                .Setup(x => x.WaitForConnectionAsync(It.IsAny<CancellationToken>()))
                .Returns(new TaskCompletionSource<Connection>().Task);

            var eventBus = new TestableEventBus();
            var dispatcher = new DefaultRequestDispatcher(connectionHost.Object, compilerHost, CancellationToken.None, eventBus, keepAlive);
            var startTime = DateTime.Now;

            // Act
            dispatcher.Run();

            // Assert
            Assert.True(eventBus.HitKeepAliveTimeout);
        }

        /// <summary>
        /// Ensure server respects keep alive and shuts down after processing a single connection.
        /// </summary>
        [Fact]
        public void KeepAliveAfterSingleConnection()
        {
            // Arrange
            var connectionTask = CreateConnectionWithEmptyBuildRequest();
            var keepAlive = TimeSpan.FromSeconds(1);
            var compilerHost = CreateCompilerHost(c =>
            {
                c.ExecuteFunc = (req, ct) =>
                {
                    return EmptyBuildResponse;
                };
            });
            var connectionHost = CreateConnectionHost(connectionTask, new TaskCompletionSource<Connection>().Task);

            var eventBus = new TestableEventBus();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None, eventBus, keepAlive);

            // Act
            dispatcher.Run();

            // Assert
            Assert.Equal(1, eventBus.CompletedCount);
            Assert.True(eventBus.LastProcessedTime.HasValue);
            Assert.True(eventBus.HitKeepAliveTimeout);
        }

        /// <summary>
        /// Ensure server respects keep alive and shuts down after processing multiple connections.
        /// </summary>
        [Fact]
        public void KeepAliveAfterMultipleConnections()
        {
            // Arrange
            var count = 5;
            var list = new List<Task<Connection>>();
            for (var i = 0; i < count; i++)
            {
                var connectionTask = CreateConnectionWithEmptyBuildRequest();
                list.Add(connectionTask);
            }

            list.Add(new TaskCompletionSource<Connection>().Task);
            var connectionHost = CreateConnectionHost(list.ToArray());
            var compilerHost = CreateCompilerHost(c =>
            {
                c.ExecuteFunc = (req, ct) =>
                {
                    return EmptyBuildResponse;
                };
            });

            var keepAlive = TimeSpan.FromSeconds(1);
            var eventBus = new TestableEventBus();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None, eventBus, keepAlive);

            // Act
            dispatcher.Run();

            // Assert
            Assert.Equal(count, eventBus.CompletedCount);
            Assert.True(eventBus.LastProcessedTime.HasValue);
            Assert.True(eventBus.HitKeepAliveTimeout);
        }

        /// <summary>
        /// Ensure server respects keep alive and shuts down after processing simultaneous connections.
        /// </summary>
        [Fact]
        public async Task KeepAliveAfterSimultaneousConnections()
        {
            // Arrange
            var totalCount = 2;
            var readySource = new TaskCompletionSource<bool>();
            var list = new List<TaskCompletionSource<bool>>();
            var connectionHost = new Mock<ConnectionHost>();
            connectionHost
                .Setup(x => x.WaitForConnectionAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) =>
                {
                    if (list.Count < totalCount)
                    {
                        var source = new TaskCompletionSource<bool>();
                        var connectionTask = CreateConnectionWithEmptyBuildRequest(c =>
                        {
                            c.WaitForDisconnectAsyncFunc = _ => source.Task;
                        });
                        list.Add(source);
                        return connectionTask;
                    }

                    readySource.SetResult(true);
                    return new TaskCompletionSource<Connection>().Task;
                });

            var compilerHost = CreateCompilerHost(c =>
            {
                c.ExecuteFunc = (req, ct) =>
                {
                    return EmptyBuildResponse;
                };
            });

            var keepAlive = TimeSpan.FromSeconds(1);
            var eventBus = new TestableEventBus();
            var dispatcherTask = Task.Run(() =>
            {
                var dispatcher = new DefaultRequestDispatcher(connectionHost.Object, compilerHost, CancellationToken.None, eventBus, keepAlive);
                dispatcher.Run();
            });

            await readySource.Task;
            foreach (var source in list)
            {
                source.SetResult(true);
            }

            // Act
            await dispatcherTask;

            // Assert
            Assert.Equal(totalCount, eventBus.CompletedCount);
            Assert.True(eventBus.LastProcessedTime.HasValue);
            Assert.True(eventBus.HitKeepAliveTimeout);
        }

        [Fact]
        public void ClientExceptionShouldBeginShutdown()
        {
            // Arrange
            var listenCancellationToken = default(CancellationToken);
            var firstConnectionTask = CreateConnectionWithEmptyBuildRequest(c =>
            {
                c.WaitForDisconnectAsyncFunc = (ct) =>
                {
                    listenCancellationToken = ct;
                    return Task.Delay(Timeout.Infinite, ct).ContinueWith<Connection>(_ => null);
                };
            });
            var secondConnectionTask = CreateConnectionWithEmptyBuildRequest(c =>
            {
                c.WaitForDisconnectAsyncFunc = (ct) => throw new Exception();
            });

            var compilerHost = CreateCompilerHost();
            var connectionHost = CreateConnectionHost(
                firstConnectionTask,
                secondConnectionTask,
                new TaskCompletionSource<Connection>().Task);
            var keepAlive = TimeSpan.FromSeconds(10);
            var eventBus = new TestableEventBus();
            var dispatcher = new DefaultRequestDispatcher(connectionHost, compilerHost, CancellationToken.None, eventBus, keepAlive);

            // Act
            dispatcher.Run();

            // Assert
            Assert.True(eventBus.HasDetectedBadConnection);
            Assert.True(listenCancellationToken.IsCancellationRequested);
        }

        private static TestableConnection CreateConnection(Stream stream, string identifier = null)
        {
            return new TestableConnection(stream, identifier ?? "identifier");
        }

        private static async Task<Connection> CreateConnectionWithEmptyBuildRequest(Action<TestableConnection> configureConnection = null)
        {
            var memoryStream = new MemoryStream();
            await EmptyBuildRequest.WriteAsync(memoryStream, CancellationToken.None);
            memoryStream.Position = 0;
            var connection = CreateConnection(memoryStream);
            configureConnection?.Invoke(connection);

            return connection;
        }

        private static ConnectionHost CreateConnectionHost(params Task<Connection>[] connections)
        {
            var host = new Mock<ConnectionHost>();
            if (connections.Length > 0)
            {
                var index = 0;
                host
                    .Setup(x => x.WaitForConnectionAsync(It.IsAny<CancellationToken>()))
                    .Returns((CancellationToken ct) => connections[index++]);
            }

            return host.Object;
        }

        private static TestableCompilerHost CreateCompilerHost(Action<TestableCompilerHost> configureCompilerHost = null)
        {
            var compilerHost = new TestableCompilerHost();
            configureCompilerHost?.Invoke(compilerHost);

            return compilerHost;
        }

        private class TestableCompilerHost : CompilerHost
        {
            internal Func<BuildRequest, CancellationToken, BuildResponse> ExecuteFunc;

            public override BuildResponse Execute(BuildRequest request, CancellationToken cancellationToken)
            {
                if (ExecuteFunc != null)
                {
                    return ExecuteFunc(request, cancellationToken);
                }

                return EmptyBuildResponse;
            }
        }

        private class TestableConnection : Connection
        {
            internal Func<CancellationToken, Task> WaitForDisconnectAsyncFunc;

            public TestableConnection(Stream stream, string identifier)
            {
                Stream = stream;
                Identifier = identifier;
                WaitForDisconnectAsyncFunc = ct => Task.Delay(Timeout.Infinite, ct);
            }

            public override Task WaitForDisconnectAsync(CancellationToken cancellationToken)
            {
                return WaitForDisconnectAsyncFunc(cancellationToken);
            }
        }

        private class TestableStream : Stream
        {
            internal readonly MemoryStream ReadStream = new MemoryStream();
            internal readonly MemoryStream WriteStream = new MemoryStream();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length { get { throw new NotImplementedException(); } }
            public override long Position
            {
                get { throw new NotImplementedException(); }
                set { throw new NotImplementedException(); }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadStream.Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadStream.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                WriteStream.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return WriteStream.WriteAsync(buffer, offset, count, cancellationToken);
            }
        }
    }
}
