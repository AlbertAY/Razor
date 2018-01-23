// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CommandLine;
using Xunit;

namespace Microsoft.AspNetCore.Razor.Tools
{
    public class BuildProtocolTest
    {
        [Fact]
        public async Task ReadWriteCompleted()
        {
            // Arrange
            var response = new CompletedBuildResponse(42, utf8output: false, output: "a string");
            var memoryStream = new MemoryStream();

            // Act
            await response.WriteAsync(memoryStream, default(CancellationToken));

            // Assert
            Assert.True(memoryStream.Position > 0);
            memoryStream.Position = 0;
            var read = (CompletedBuildResponse)await BuildResponse.ReadAsync(memoryStream, default(CancellationToken));
            Assert.Equal(42, read.ReturnCode);
            Assert.False(read.Utf8Output);
            Assert.Equal("a string", read.Output);
            Assert.Equal("", read.ErrorOutput);
        }

        [Fact]
        public async Task ReadWriteRequest()
        {
            // Arrange
            var request = new BuildRequest(
                BuildProtocolConstants.ProtocolVersion,
                ImmutableArray.Create(
                    new BuildRequest.Argument(BuildProtocolConstants.ArgumentId.CurrentDirectory, argumentIndex: 0, value: "directory"),
                    new BuildRequest.Argument(BuildProtocolConstants.ArgumentId.CommandLineArgument, argumentIndex: 1, value: "file")));
            var memoryStream = new MemoryStream();

            // Act
            await request.WriteAsync(memoryStream, default(CancellationToken));

            // Assert
            Assert.True(memoryStream.Position > 0);
            memoryStream.Position = 0;
            var read = await BuildRequest.ReadAsync(memoryStream, default(CancellationToken));
            Assert.Equal(BuildProtocolConstants.ProtocolVersion, read.ProtocolVersion);
            Assert.Equal(2, read.Arguments.Count);
            Assert.Equal(BuildProtocolConstants.ArgumentId.CurrentDirectory, read.Arguments[0].ArgumentId);
            Assert.Equal(0, read.Arguments[0].ArgumentIndex);
            Assert.Equal("directory", read.Arguments[0].Value);
            Assert.Equal(BuildProtocolConstants.ArgumentId.CommandLineArgument, read.Arguments[1].ArgumentId);
            Assert.Equal(1, read.Arguments[1].ArgumentIndex);
            Assert.Equal("file", read.Arguments[1].Value);
        }

        [Fact]
        public void ShutdownMessage()
        {
            // Arrange & Act
            var request = BuildRequest.CreateShutdown();

            // Assert
            Assert.Equal(2, request.Arguments.Count);

            var argument1 = request.Arguments[0];
            Assert.Equal(BuildProtocolConstants.ArgumentId.Shutdown, argument1.ArgumentId);
            Assert.Equal(0, argument1.ArgumentIndex);
            Assert.Equal("", argument1.Value);

            var argument2 = request.Arguments[1];
            Assert.Equal(BuildProtocolConstants.ArgumentId.CommandLineArgument, argument2.ArgumentId);
            Assert.Equal(1, argument2.ArgumentIndex);
            Assert.Equal("shutdown", argument2.Value);
        }

        [Fact]
        public async Task ShutdownRequestWriteRead()
        {
            // Arrange
            var memoryStream = new MemoryStream();
            var request = BuildRequest.CreateShutdown();

            // Act
            await request.WriteAsync(memoryStream, CancellationToken.None);

            // Assert
            memoryStream.Position = 0;
            var read = await BuildRequest.ReadAsync(memoryStream, CancellationToken.None);

            var argument1 = request.Arguments[0];
            Assert.Equal(BuildProtocolConstants.ArgumentId.Shutdown, argument1.ArgumentId);
            Assert.Equal(0, argument1.ArgumentIndex);
            Assert.Equal("", argument1.Value);

            var argument2 = request.Arguments[1];
            Assert.Equal(BuildProtocolConstants.ArgumentId.CommandLineArgument, argument2.ArgumentId);
            Assert.Equal(1, argument2.ArgumentIndex);
            Assert.Equal("shutdown", argument2.Value);
        }

        [Fact]
        public async Task ShutdownResponseWriteRead()
        {
            // Arrange & Act 1
            var memoryStream = new MemoryStream();
            var response = new ShutdownBuildResponse(42);

            // Assert 1
            Assert.Equal(BuildResponse.ResponseType.Shutdown, response.Type);

            // Act 2
            await response.WriteAsync(memoryStream, CancellationToken.None);

            // Assert 2
            memoryStream.Position = 0;
            var read = await BuildResponse.ReadAsync(memoryStream, CancellationToken.None);
            Assert.Equal(BuildResponse.ResponseType.Shutdown, read.Type);
            var typed = (ShutdownBuildResponse)read;
            Assert.Equal(42, typed.ServerProcessId);
        }
    }
}
