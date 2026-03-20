using FluentAssertions;
using WpfBrowserWorker.Models;
using WpfBrowserWorker.Services;

namespace WpfBrowserWorker.Tests;

public class WorkerStateServiceTests
{
    [Fact]
    public void RecordTaskResult_Success_IncrementsCompleted()
    {
        var svc = new WorkerStateService();
        svc.RecordTaskResult(TaskResult.Succeed());
        svc.TasksCompleted.Should().Be(1);
        svc.TasksFailed.Should().Be(0);
    }

    [Fact]
    public void RecordTaskResult_Failure_IncrementsFailed()
    {
        var svc = new WorkerStateService();
        svc.RecordTaskResult(TaskResult.Fail("error"));
        svc.TasksCompleted.Should().Be(0);
        svc.TasksFailed.Should().Be(1);
    }

    [Fact]
    public void TrackAndUntrack_Task_UpdatesActiveTasks()
    {
        var svc = new WorkerStateService();
        svc.TrackTask("task-1", "acc-1", "like");
        svc.ActiveTasks.Should().ContainKey("task-1");
        svc.UntrackTask("task-1");
        svc.ActiveTasks.Should().NotContainKey("task-1");
    }

    [Fact]
    public void SetConnected_RaisesStateChanged()
    {
        var svc = new WorkerStateService();
        var raised = false;
        svc.StateChanged += (_, _) => raised = true;
        svc.SetConnected(true);
        raised.Should().BeTrue();
        svc.IsConnected.Should().BeTrue();
    }
}
