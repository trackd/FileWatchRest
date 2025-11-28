using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FileWatchRest.Models;
using FluentAssertions;
using Xunit;

namespace FileWatchRest.Tests.Action;

public class ExecutableActionTests {
    [Fact]
    public async Task ExecuteAsync_SubstitutesTokensAndInvokesProcessOverride() {
        var fileEvent = new FileEventRecord("C:\\temp\\file.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> { "-p", "{FilePath}", "--json", "{FileNotification:json}", "const" };
        var action = new FileWatchRest.Services.ExecutableAction("myexe", args);

        ProcessStartInfo? captured = null;
        FileWatchRest.Services.ExecutableAction.StartProcessAsyncOverride = (startInfo, ct) => {
            captured = startInfo;
            return Task.CompletedTask;
        };

        try {
            await action.ExecuteAsync(fileEvent, CancellationToken.None);

            captured.Should().NotBeNull();
            captured!.FileName.Should().Be("myexe");
            captured.ArgumentList.Should().Contain("-p");
            captured.ArgumentList.Should().Contain("C:\\temp\\file.txt");
            captured.ArgumentList.Should().Contain(arg => arg.Contains("\"Path\"") && arg.Contains("file.txt"));
        }
        finally {
            FileWatchRest.Services.ExecutableAction.StartProcessAsyncOverride = null;
        }
    }
}
