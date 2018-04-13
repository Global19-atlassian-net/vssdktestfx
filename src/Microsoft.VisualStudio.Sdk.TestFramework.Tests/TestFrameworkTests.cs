﻿/********************************************************
*                                                        *
*   © Copyright (C) Microsoft. All rights reserved.      *
*                                                        *
*********************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Sdk.TestFramework;
using Microsoft.VisualStudio.Sdk.TestFramework.Tests;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Xunit;
using OleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;
using Task = System.Threading.Tasks.Task;

[Collection(MockedVS.Collection)]
public class TestFrameworkTests
{
    private readonly MefHosting mef;

    public TestFrameworkTests(GlobalServiceProvider sp, MefHosting mef)
    {
        this.mef = mef;
        sp.Reset();
    }

    [Fact]
    public void OleServiceProviderIsService()
    {
        object sp = ServiceProvider.GlobalProvider.GetService(typeof(OleServiceProvider));
        Assert.IsAssignableFrom<OleServiceProvider>(sp);
    }

    [Fact]
    public async Task MainThreadHandling()
    {
        if (ThreadHelper.JoinableTaskContext.MainThread == Thread.CurrentThread)
        {
            // Get off the "main thread" so we can switch back.
            await TaskScheduler.Default;
            Assert.NotSame(ThreadHelper.JoinableTaskContext.MainThread, Thread.CurrentThread);
        }

        ThreadHelper.ThrowIfOnUIThread();
        Assert.Throws<COMException>(() => ThreadHelper.ThrowIfNotOnUIThread());

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        Assert.Same(ThreadHelper.JoinableTaskContext.MainThread, Thread.CurrentThread);

        ThreadHelper.ThrowIfNotOnUIThread();
        Assert.Throws<COMException>(() => ThreadHelper.ThrowIfOnUIThread());
    }

    [Fact]
    public async Task TestAssemblyIsInMefCatalog()
    {
        var ep = await this.mef.CreateExportProviderAsync();
        Assert.NotNull(ep.GetExportedValue<SomeMefExport>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThreadHelper_Invoke(bool fromMainThread)
    {
        if (ThreadHelper.CheckAccess() && !fromMainThread)
        {
            // Get off the "main thread" so we can switch back.
            await TaskScheduler.Default;
            Assert.False(ThreadHelper.CheckAccess());
        }
        else if (fromMainThread && !ThreadHelper.CheckAccess())
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Assert.True(ThreadHelper.CheckAccess());
        }

        bool delegateExecuted = false;
        ThreadHelper.Generic.Invoke(delegate
        {
            Assert.True(ThreadHelper.CheckAccess());
            ThreadHelper.ThrowIfNotOnUIThread();
            delegateExecuted = true;
        });
        Assert.True(delegateExecuted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThreadHelper_Invoke_PropagatesExceptions(bool fromMainThread)
    {
        if (ThreadHelper.CheckAccess() && !fromMainThread)
        {
            // Get off the "main thread" so we can switch back.
            await TaskScheduler.Default;
            Assert.False(ThreadHelper.CheckAccess());
        }
        else if (fromMainThread && !ThreadHelper.CheckAccess())
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Assert.True(ThreadHelper.CheckAccess());
        }

        // Verify that exceptions are propagated.
        Assert.Throws<ApplicationException>(() =>
        {
            ThreadHelper.Generic.Invoke(delegate
            {
                Assert.True(ThreadHelper.CheckAccess());
                ThreadHelper.ThrowIfNotOnUIThread();
                throw new ApplicationException();
            });
        });
    }

    [Fact]
    public async Task ThreadHelper_BeginInvoke()
    {
        var delegateExecuted = new AsyncManualResetEvent();
        ThreadHelper.Generic.BeginInvoke(delegate
        {
            delegateExecuted.Set();
        });
        await delegateExecuted.WaitAsync();
    }

    [Fact]
    public async Task ThreadHelper_InvokeAsync()
    {
        bool delegateExecuted = false;
        var t = ThreadHelper.Generic.InvokeAsync(delegate
        {
            delegateExecuted = true;
        });
        await t;
        Assert.True(delegateExecuted);
    }

    [Fact]
    public async Task VsTaskScheduler_UISchedulersWork()
    {
        await ThreadHelper.JoinableTaskFactory.RunAsync(
            VsTaskRunContext.UIThreadIdlePriority,
            async delegate
            {
                await Task.Yield();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            });
    }

    [Fact]
    public async Task RunAsyncAsVsTask()
    {
        bool result = (bool)await ThreadHelper.JoinableTaskFactory.RunAsyncAsVsTask(
            VsTaskRunContext.UIThreadNormalPriority,
            async ct =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Assert.True(ThreadHelper.CheckAccess());
                return true;
            });
        Assert.True(result);
    }

    [Fact]
    public async Task ExplicitVsTaskCreation()
    {
        IVsTask vsTask = VsTaskLibraryHelper.CreateAndStartTask(
            VsTaskLibraryHelper.ServiceInstance,
            VsTaskRunContext.UIThreadNormalPriority,
            VsTaskLibraryHelper.CreateTaskBody(() => Assert.True(ThreadHelper.CheckAccess())));
        await vsTask;
    }

    [Fact]
    public async Task AwaitingIVsTaskPreservesContext()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        await ThreadHelper.JoinableTaskFactory.RunAsyncAsVsTask(
            VsTaskRunContext.UIThreadBackgroundPriority,
            async ct =>
            {
                await Task.Delay(100);
                return 0;
            });
        ThreadHelper.ThrowIfNotOnUIThread();
    }
}
