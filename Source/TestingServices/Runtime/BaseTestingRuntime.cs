﻿//-----------------------------------------------------------------------
// <copyright file="BaseTestingRuntime.cs">
//      Copyright (c) Microsoft Corporation. All rights reserved.
//
//      THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//      EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//      MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
//      IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
//      CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
//      TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
//      SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Microsoft.PSharp.Runtime;
using Microsoft.PSharp.TestingServices.Coverage;
using Microsoft.PSharp.TestingServices.Scheduling;
using Microsoft.PSharp.TestingServices.SchedulingStrategies;
using Microsoft.PSharp.TestingServices.StateCaching;
using Microsoft.PSharp.TestingServices.Tracing.Error;
using Microsoft.PSharp.TestingServices.Tracing.Schedule;

namespace Microsoft.PSharp.TestingServices.Runtime
{
    /// <summary>
    /// The base P# testing runtime.
    /// </summary>
    internal abstract class BaseTestingRuntime : BaseRuntime, ITestingRuntime
    {
        /// <summary>
        /// The asynchronous task scheduler.
        /// </summary>
        internal readonly AsynchronousTaskScheduler TaskScheduler;

        /// <summary>
        /// Interface for registering runtime operations.
        /// </summary>
        internal readonly IRegisterRuntimeOperation Reporter;

        /// <summary>
        /// The P# program state cache.
        /// </summary>
        internal readonly StateCache StateCache;

        /// <summary>
        /// List of monitors in the program.
        /// </summary>
        private readonly List<Monitor> Monitors;

        /// <summary>
        /// Map from task ids to machines.
        /// </summary>
        protected readonly ConcurrentDictionary<int, BaseMachine> TaskMap;

        /// <summary>
        /// Set of all machine Ids created by this runtime.
        /// </summary>
        internal readonly HashSet<MachineId> CreatedMachineIds;

        /// <summary>
        /// The root task id.
        /// </summary>
        internal readonly int? RootTaskId;

        /// <summary>
        /// The scheduler used to serialize the execution of
        /// the program, and explore schedules to find bugs.
        /// </summary>
        public BugFindingScheduler Scheduler { get; }

        /// <summary>
        /// The P# program schedule trace.
        /// </summary>
        public ScheduleTrace ScheduleTrace { get; }

        /// <summary>
        /// The bug trace.
        /// </summary>
        public BugTrace BugTrace { get; }

        /// <summary>
        /// Data structure containing information
        /// regarding testing coverage.
        /// </summary>
        public CoverageInfo CoverageInfo { get; }

        /// <summary>
        /// Constructor.
        /// <param name="configuration">Configuration</param>
        /// <param name="strategy">SchedulingStrategy</param>
        /// <param name="reporter">Reporter to register runtime operations.</param>
        /// </summary>
        protected BaseTestingRuntime(Configuration configuration, ISchedulingStrategy strategy, IRegisterRuntimeOperation reporter)
            : base(configuration)
        {
            this.Monitors = new List<Monitor>();
            this.TaskMap = new ConcurrentDictionary<int, BaseMachine>();
            this.RootTaskId = Task.CurrentId;
            this.CreatedMachineIds = new HashSet<MachineId>();

            this.ScheduleTrace = new ScheduleTrace();
            this.BugTrace = new BugTrace();
            this.StateCache = new StateCache(this);

            this.TaskScheduler = new AsynchronousTaskScheduler(this, this.TaskMap);
            this.CoverageInfo = new CoverageInfo();
            this.Reporter = reporter;

            if (!(strategy is DPORStrategy) && !(strategy is ReplayStrategy))
            {
                var reductionStrategy = BasicReductionStrategy.ReductionStrategy.None;
                if (configuration.ReductionStrategy == Utilities.ReductionStrategy.OmitSchedulingPoints)
                {
                    reductionStrategy = BasicReductionStrategy.ReductionStrategy.OmitSchedulingPoints;
                }
                else if (configuration.ReductionStrategy == Utilities.ReductionStrategy.ForceSchedule)
                {
                    reductionStrategy = BasicReductionStrategy.ReductionStrategy.ForceSchedule;
                }

                strategy = new BasicReductionStrategy(strategy, reductionStrategy);
            }

            if (configuration.EnableLivenessChecking && configuration.EnableCycleDetection)
            {
                this.Scheduler = new BugFindingScheduler(this, new CycleDetectionStrategy(
                    configuration, this.StateCache, this.ScheduleTrace, this.Monitors, strategy));
            }
            else if (configuration.EnableLivenessChecking)
            {
                this.Scheduler = new BugFindingScheduler(this, new TemperatureCheckingStrategy(
                    configuration, this.Monitors, strategy));
            }
            else
            {
                this.Scheduler = new BugFindingScheduler(this, strategy);
            }
        }

        #region runtime interface

        /// <summary>
        /// Runs a test harness that executes the specified test method.
        /// </summary>
        /// <param name="testMethod">MethodInfo</param>
        void ITestingRuntime.RunTestHarness(MethodInfo testMethod)
        {
            this.Assert(Task.CurrentId != null, "The test harness machine must execute inside a task.");
            this.Assert(testMethod != null, "The test harness machine cannot execute a null test method.");
            MachineId mid = new MachineId(this, typeof(TestHarnessMachine), null);
            TestHarnessMachine harness = new TestHarnessMachine(testMethod);
            harness.Initialize(this, mid, new SchedulableInfo(mid));
            this.RunTestHarness(harness);
        }

        /// <summary>
        /// Runs a test harness that executes the specified test action.
        /// </summary>
        /// <param name="testAction">Action</param>
        void ITestingRuntime.RunTestHarness(Action<IPSharpRuntime> testAction)
        {
            this.Assert(Task.CurrentId != null, "The test harness machine must execute inside a task.");
            this.Assert(testAction != null, "The test harness machine cannot execute a null test action.");
            MachineId mid = new MachineId(this, typeof(TestHarnessMachine), null);
            TestHarnessMachine harness = new TestHarnessMachine(testAction);
            harness.Initialize(this, mid, new SchedulableInfo(mid));
            this.RunTestHarness(harness);
        }

        /// <summary>
        /// Creates a new machine of the specified type and with
        /// the specified optional event. This event can only be
        /// used to access its payload, and cannot be handled.
        /// </summary>
        /// <param name="type">Type of the machine.</param>
        /// <param name="operationGroupId">Optional operation group id.</param>
        /// <param name="e">Event</param>
        /// <returns>Task that represents the asynchronous operation. The task result is the <see cref="MachineId"/>.</returns>
        public override Task<MachineId> CreateMachineAsync(Type type, Event e = null, Guid? operationGroupId = null)
        {
            return this.CreateMachineAsync(null, type, null, e, operationGroupId);
        }

        /// <summary>
        /// Creates a new machine of the specified type and name, and
        /// with the specified optional event. This event can only be
        /// used to access its payload, and cannot be handled.
        /// </summary>
        /// <param name="type">Type of the machine.</param>
        /// <param name="friendlyName">Friendly machine name used for logging.</param>
        /// <param name="operationGroupId">Optional operation group id.</param>
        /// <param name="e">Event</param>
        /// <returns>Task that represents the asynchronous operation. The task result is the <see cref="MachineId"/>.</returns>
        public override Task<MachineId> CreateMachineAsync(Type type, string friendlyName, Event e = null, Guid? operationGroupId = null)
        {
            return this.CreateMachineAsync(null, type, friendlyName, e, operationGroupId);
        }

        /// <summary>
        /// Creates a new machine of the specified type, using the specified <see cref="MachineId"/>.
        /// This method optionally passes an <see cref="Event"/> to the new machine, which can only
        /// be used to access its payload, and cannot be handled.
        /// </summary>
        /// <param name="mid">Unbound machine id.</param>
        /// <param name="type">Type of the machine.</param>
        /// <param name="e">Event</param>
        /// <param name="operationGroupId">Optional operation group id.</param>
        /// <returns>Task that represents the asynchronous operation. The task result is the <see cref="MachineId"/>.</returns>
        public override Task<MachineId> CreateMachineAsync(MachineId mid, Type type, Event e = null, Guid? operationGroupId = null)
        {
            this.Assert(mid != null, "Cannot pass a null MachineId.");
            return this.CreateMachineAsync(mid, type, mid.FriendlyName, e, operationGroupId);
        }

        /// <summary>
        /// Sends an asynchronous <see cref="Event"/> to a machine.
        /// </summary>
        /// <param name="target">Target machine id</param>
        /// <param name="e">Event</param>
        /// <param name="options">Optional parameters of a send operation.</param>
        /// <returns>Task that represents the asynchronous operation.</returns>
        public override Task SendEventAsync(MachineId target, Event e, SendOptions options = null)
        {
            // If the target machine is null then report an error and exit.
            this.Assert(target != null, "Cannot send to a null machine.");
            // If the event is null then report an error and exit.
            this.Assert(e != null, "Cannot send a null event.");
            return this.SendEventAsync(target, e, this.GetCurrentMachine(), options);
        }

        /// <summary>
        /// Registers a new specification monitor of the specified <see cref="Type"/>.
        /// </summary>
        /// <param name="type">Type of the monitor</param>
        public override void RegisterMonitor(Type type)
        {
            this.TryCreateMonitor(type);
        }

        /// <summary>
        /// Invokes the specified monitor with the specified <see cref="Event"/>.
        /// </summary>
        /// <typeparam name="T">Type of the monitor</typeparam>
        /// <param name="e">Event</param>
        public override void InvokeMonitor<T>(Event e)
        {
            this.InvokeMonitor(typeof(T), e);
        }

        /// <summary>
        /// Invokes the specified monitor with the specified <see cref="Event"/>.
        /// </summary>
        /// <param name="type">Type of the monitor</param>
        /// <param name="e">Event</param>
        public override void InvokeMonitor(Type type, Event e)
        {
            // If the event is null then report an error and exit.
            this.Assert(e != null, "Cannot monitor a null event.");
            this.Monitor(type, null, e);
        }

        /// <summary>
        /// Returns the operation group id of the specified machine. Returns <see cref="Guid.Empty"/>
        /// if the id is not set, or if the <see cref="MachineId"/> is not associated with this runtime.
        /// During testing, the runtime asserts that the specified machine is currently executing.
        /// </summary>
        /// <param name="currentMachine">MachineId of the currently executing machine.</param>
        /// <returns>Guid</returns>
        public override Guid GetCurrentOperationGroupId(MachineId currentMachine)
        {
            this.Assert(currentMachine == this.GetCurrentMachineId(), "Trying to access the operation group id of " +
                $"'{currentMachine}', which is not the currently executing machine.");

            if (!this.MachineMap.TryGetValue(currentMachine, out BaseMachine machine))
            {
                return Guid.Empty;
            }

            return machine.Info.OperationGroupId;
        }

        /// <summary>
        /// Gets the id of the currently executing machine.
        /// </summary>
        /// <returns>MachineId or null, if not present</returns>
        public MachineId GetCurrentMachineId()
        {
            return this.GetCurrentMachine()?.Id;
        }

        /// <summary>
        /// Gets the currently executing machine.
        /// </summary>
        /// <returns>The machine, or null if not present.</returns>
        public BaseMachine GetCurrentMachine()
        {
            //  The current task does not correspond to a machine.
            if (Task.CurrentId == null)
            {
                return null;
            }

            // The current task does not correspond to a machine.
            if (!this.TaskMap.ContainsKey((int)Task.CurrentId))
            {
                return null;
            }

            BaseMachine current = this.TaskMap[(int)Task.CurrentId];

            // The current task does not correspond to a machine.
            if (!(current is BaseMachine machine))
            {
                return null;
            }

            return machine;
        }

        /// <summary>
        /// Checks that no monitor is in a hot state upon program termination.
        /// If the program is still running, then this method returns without
        /// performing a check.
        /// </summary>
        void ITestingRuntime.CheckNoMonitorInHotStateAtTermination()
        {
            if (!this.Scheduler.HasFullyExploredSchedule)
            {
                return;
            }

            foreach (var monitor in this.Monitors)
            {
                if (monitor.IsInHotState(out string stateName))
                {
                    string message = IO.Utilities.Format("Monitor '{0}' detected liveness bug " +
                        "in hot state '{1}' at the end of program execution.",
                        monitor.GetType().Name, stateName);
                    this.Scheduler.NotifyAssertionFailure(message, false);
                }
            }
        }

        /// <summary>
        /// Waits until all P# machines have finished execution.
        /// </summary>
        void ITestingRuntime.Wait()
        {
            this.Scheduler.Wait();
            this.IsRunning = false;
        }

        #endregion

        #region machine creation and execution

        /// <summary>
        /// Runs the specified test harness. 
        /// </summary>
        /// <param name="harness">The test harness machine.</param>
        private void RunTestHarness(TestHarnessMachine harness)
        {
            Task task = new Task(() =>
            {
                try
                {
                    this.Scheduler.NotifyEventHandlerStarted(harness.Info as SchedulableInfo);

                    harness.Run();

                    IO.Debug.WriteLine($"<ScheduleDebug> Completed event handler of the test harness machine.");
                    (harness.Info as SchedulableInfo).NotifyEventHandlerCompleted();
                    this.Scheduler.Schedule(OperationType.Stop, OperationTargetType.Schedulable, harness.Info.Id);
                    IO.Debug.WriteLine($"<ScheduleDebug> Exit event handler of the test harness machine.");
                }
                catch (ExecutionCanceledException)
                {
                    IO.Debug.WriteLine($"<Exception> ExecutionCanceledException was thrown in the test harness.");
                }
                catch (Exception ex)
                {
                    harness.ReportUnhandledException(ex);
                }
            });

            (harness.Info as SchedulableInfo).NotifyEventHandlerCreated(task.Id, 0);
            this.Scheduler.NotifyEventHandlerCreated(harness.Info as SchedulableInfo);

            task.Start();

            this.Scheduler.WaitForEventHandlerToStart(harness.Info as SchedulableInfo);
        }

        /// <summary>
        /// Creates a new machine of the specified <see cref="Type"/> and name, using the specified
        /// unbound machine id, and passes the specified optional <see cref="Event"/>. This event
        /// can only be used to access its payload, and cannot be handled.
        /// </summary>
        /// <param name="mid">Unbound machine id.</param>
        /// <param name="type">Type of the machine.</param>
        /// <param name="friendlyName">Friendly machine name used for logging.</param>
        /// <param name="operationGroupId">Optional operation group id.</param>
        /// <param name="e">Event</param>
        /// <returns>Task that represents the asynchronous operation. The task result is the <see cref="MachineId"/>.</returns>
        protected Task<MachineId> CreateMachineAsync(MachineId mid, Type type, string friendlyName, Event e = null,
            Guid? operationGroupId = null)
        {
            BaseMachine creator = null;
            if (this.TaskMap.ContainsKey((int)Task.CurrentId))
            {
                creator = this.TaskMap[(int)Task.CurrentId];
            }

            return this.CreateMachineAsync(mid, type, friendlyName, e, creator, operationGroupId);
        }

        /// <summary>
        /// Creates a new machine of the specified <see cref="Type"/>.
        /// </summary>
        /// <param name="mid">Unbound machine id.</param>
        /// <param name="type">Type of the machine.</param>
        /// <param name="friendlyName">Friendly machine name used for logging.</param>
        /// <param name="operationGroupId">The operation group id.</param>
        /// <param name="e">Event passed during machine construction.</param>
        /// <param name="creator">The creator machine.</param>
        /// <returns>Task that represents the asynchronous operation. The task result is the <see cref="MachineId"/>.</returns>
        protected internal override async Task<MachineId> CreateMachineAsync(MachineId mid, Type type, string friendlyName,
            Event e, BaseMachine creator, Guid? operationGroupId)
        {
            this.CheckMachineMethodInvocation(creator, MachineApiNames.CreateMachineApiName);

            // Using ulong.MaxValue because a 'Create' operation cannot specify
            // the id of its target, because the id does not exist yet.
            this.Scheduler.Schedule(OperationType.Create, OperationTargetType.Schedulable, ulong.MaxValue);

            BaseMachine machine = await this.CreateMachineAsync(mid, type, friendlyName, creator);
            this.SetOperationGroupIdForMachine(machine, creator, operationGroupId);

            this.BugTrace.AddCreateMachineStep(creator, machine.Id, e == null ? null : new EventInfo(e));
            this.RunMachineEventHandler(machine, e, true, null, null);

            return machine.Id;
        }

        /// <summary>
        /// Creates a new machine of the specified <see cref="Type"/>.
        /// </summary>
        /// <param name="mid">Unbound machine id.</param>
        /// <param name="type">Type of the machine.</param>
        /// <param name="friendlyName">Friendly machine name used for logging.</param>
        /// <param name="creator">The id of the machine that created the returned machine.</param>
        /// <returns>Task that represents the asynchronous operation. The task result is the machine.</returns>
        protected async Task<BaseMachine> CreateMachineAsync(MachineId mid, Type type, string friendlyName, BaseMachine creator)
        {
            this.Assert(this.IsSupportedMachineType(type), "Type '{0}' is not a machine.", type.Name);

            if (mid == null)
            {
                mid = new MachineId(this, type, friendlyName);
            }
            else
            {
                this.Assert(mid.Runtime == null || mid.Runtime == this, "Unbound machine id '{0}' was created by another runtime.", mid.Value);
                this.Assert(mid.Type == type.FullName, "Cannot bind machine id '{0}' of type '{1}' to a machine of type '{2}'.",
                    mid.Value, mid.Type, type.FullName);
                mid.Bind(this);
            }

            var isMachineTypeCached = this.IsMachineConstructorCached(type);
            BaseMachine machine = await this.CreateMachineAsync(mid, type);

            if (this.Configuration.ReportActivityCoverage && !isMachineTypeCached)
            {
                this.ReportActivityCoverageOfMachine(machine);
            }

            bool result = this.MachineMap.TryAdd(mid, machine);
            this.Assert(result, "Machine with id '{0}' is already bound to an existing machine.", mid.Value);

            this.Assert(!this.CreatedMachineIds.Contains(mid), "MachineId '{0}' of a previously halted machine cannot be reused " +
                "to create a new machine of type {1}", mid.Value, type.FullName);
            this.CreatedMachineIds.Add(mid);

            this.Logger.OnCreateMachine(mid, creator?.Id);

            if (this.Configuration.EnableDataRaceDetection)
            {
                Reporter.RegisterCreateMachine(creator?.Id, mid);
            }

            return machine;
        }

        /// <summary>
        /// Sends an asynchronous <see cref="Event"/> to a machine.
        /// </summary>
        /// <param name="mid">MachineId</param>
        /// <param name="e">Event</param>
        /// <param name="sender">The sender machine.</param>
        /// <param name="options">Optional parameters of a send operation.</param>
        /// <returns>Task that represents the asynchronous operation.</returns>
        protected internal override Task SendEventAsync(MachineId mid, Event e, BaseMachine sender, SendOptions options)
        {
            this.CheckMachineMethodInvocation(sender, MachineApiNames.SendEventApiName);
            this.Assert(this.CreatedMachineIds.Contains(mid), "Cannot Send event {0} to a MachineId '{1}' that was never " +
                "previously bound to a machine of type {2}", e.GetType().FullName, mid.Value, mid);

            this.Scheduler.Schedule(OperationType.Send, OperationTargetType.Inbox, mid.Value);
            var operationGroupId = this.GetNewOperationGroupId(sender, options?.OperationGroupId);

            if (this.GetTargetMachine(mid, e, sender, operationGroupId, out BaseMachine machine))
            {
                bool runNewHandler = false;
                EventInfo eventInfo = this.EnqueueEvent(machine, e, sender, operationGroupId, options?.MustHandle ?? false, ref runNewHandler);
                if (runNewHandler)
                {
                    this.RunMachineEventHandler(machine, null, false, null, eventInfo);
                }
            }
            else
            {
                this.Assert(options == null || !options.MustHandle,
                    $"A must-handle event '{e.GetType().Name}' was sent to the halted machine '{mid}'.\n");
            }

#if NET45
            return Task.FromResult(0);
#else
            return Task.CompletedTask;
#endif
        }

        /// <summary>
        /// Enqueues an asynchronous <see cref="Event"/> to a machine.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="e">Event</param>
        /// <param name="sender">The sender machine.</param>
        /// <param name="operationGroupId">The operation group id.</param>
        /// <param name="mustHandle">MustHandle event</param>
        /// <param name="runNewHandler">Run a new handler</param>
        /// <returns>EventInfo</returns>
        protected EventInfo EnqueueEvent(BaseMachine machine, Event e, BaseMachine sender, Guid operationGroupId,
            bool mustHandle, ref bool runNewHandler)
        {
            EventOriginInfo originInfo = null;
            if (sender != null)
            {
                originInfo = new EventOriginInfo(sender.Id, sender.GetType().Name,
                    sender.CurrentState == null ? "None" :
                    StateGroup.GetQualifiedStateName(sender.CurrentState));
            }
            else
            {
                // Message comes from outside P#.
                originInfo = new EventOriginInfo(null, "Env", "Env");
            }

            EventInfo eventInfo = new EventInfo(e, originInfo, Scheduler.ScheduledSteps);
            eventInfo.SetOperationGroupId(operationGroupId);
            eventInfo.SetMustHandle(mustHandle);

            this.Logger.OnSend(machine.Id, sender?.Id, sender?.CurrentStateName ?? String.Empty,
                e.GetType().FullName, operationGroupId, isTargetHalted: false);

            if (sender != null)
            {
                this.BugTrace.AddSendEventStep(sender.Id, sender.CurrentStateName, eventInfo, machine.Id);
                if (this.Configuration.EnableDataRaceDetection)
                {
                    this.Reporter.RegisterEnqueue(sender.Id, machine.Id, e, (ulong)Scheduler.ScheduledSteps);
                }
            }

            machine.Enqueue(eventInfo, ref runNewHandler);

            return eventInfo;
        }

        /// <summary>
        /// Runs a new asynchronous machine event handler.
        /// This is a fire and forget invocation.
        /// </summary>
        /// <param name="machine">The machine that executes this event handler.</param>
        /// <param name="initialEvent">Event for initializing the machine.</param>
        /// <param name="isFresh">If true, then this is a new machine.</param>
        /// <param name="syncCaller">Caller machine that is blocked for quiescence.</param>
        /// <param name="enablingEvent">If non-null, the event info of the sent event that caused the event handler to be restarted.</param>
        protected void RunMachineEventHandler(BaseMachine machine, Event initialEvent, bool isFresh, MachineId syncCaller, EventInfo enablingEvent)
        {
            Task task = new Task(async () =>
            {
                try
                {
                    this.Scheduler.NotifyEventHandlerStarted(machine.Info as SchedulableInfo);

                    if (isFresh)
                    {
                        await machine.GotoStartStateAsync(initialEvent);
                    }

                    await machine.RunEventHandlerAsync();

                    if (syncCaller != null)
                    {
                        this.SendEvent(syncCaller, new QuiescentEvent(machine.Id));
                    }

                    IO.Debug.WriteLine($"<ScheduleDebug> Completed event handler of '{machine.Id}'.");
                    (machine.Info as SchedulableInfo).NotifyEventHandlerCompleted();

                    if (machine.Info.IsHalted)
                    {
                        this.Scheduler.Schedule(OperationType.Stop, OperationTargetType.Schedulable, machine.Info.Id);
                    }
                    else
                    {
                        this.Scheduler.Schedule(OperationType.Receive, OperationTargetType.Inbox, machine.Info.Id);
                    }

                    IO.Debug.WriteLine($"<ScheduleDebug> Exit event handler of '{machine.Id}'.");
                }
                catch (ExecutionCanceledException)
                {
                    IO.Debug.WriteLine($"<Exception> ExecutionCanceledException was thrown from machine '{machine.Id}'.");
                }
                finally
                {
                    this.TaskMap.TryRemove(Task.CurrentId.Value, out machine);
                }
            });

            this.TaskMap.TryAdd(task.Id, machine);

            (machine.Info as SchedulableInfo).NotifyEventHandlerCreated(task.Id, enablingEvent?.SendStep ?? 0);
            this.Scheduler.NotifyEventHandlerCreated(machine.Info as SchedulableInfo);

            task.Start(this.TaskScheduler);

            this.Scheduler.WaitForEventHandlerToStart(machine.Info as SchedulableInfo);
        }

        #endregion

        #region specifications and error checking

        /// <summary>
        /// Tries to create a new monitor of the given type.
        /// </summary>
        /// <param name="type">Type of the monitor</param>
        private void TryCreateMonitor(Type type)
        {
            this.Assert(type.IsSubclassOf(typeof(Monitor)), $"Type '{type.Name}' " +
                "is not a subclass of Monitor.\n");

            MachineId mid = new MachineId(this, type, null);

            SchedulableInfo info = new SchedulableInfo(mid);
            Scheduler.NotifyMonitorRegistered(info);

            Monitor monitor = Activator.CreateInstance(type) as Monitor;
            monitor.Initialize(mid);
            monitor.InitializeStateInformation();

            this.Logger.OnCreateMonitor(type.Name, monitor.Id);

            this.ReportActivityCoverageOfMonitor(monitor);
            this.BugTrace.AddCreateMonitorStep(mid);

            this.Monitors.Add(monitor);

            monitor.GotoStartState();
        }

        /// <summary>
        /// Invokes the specified monitor with the given event.
        /// </summary>
        /// <param name="type">Type of the monitor.</param>
        /// <param name="invoker">The machine invoking the monitor.</param>
        /// <param name="e">Event sent to the monitor.</param>
        protected internal override void Monitor(Type type, BaseMachine invoker, Event e)
        {
            this.CheckMachineMethodInvocation(invoker, MachineApiNames.MonitorEventApiName);

            foreach (var m in this.Monitors)
            {
                if (m.GetType() == type)
                {
                    if (this.Configuration.ReportActivityCoverage)
                    {
                        this.ReportActivityCoverageOfMonitorEvent(invoker, m, e);
                        this.ReportActivityCoverageOfMonitorTransition(m, e);
                    }
                    if (this.Configuration.EnableDataRaceDetection)
                    {
                        this.Reporter.InMonitor = (long)m.Id.Value;
                    }

                    m.MonitorEvent(e);
                    if (this.Configuration.EnableDataRaceDetection)
                    {
                        this.Reporter.InMonitor = -1;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the assertion holds, and if not it throws an
        /// <see cref="AssertionFailureException"/> exception.
        /// </summary>
        /// <param name="predicate">Predicate</param>
        public override void Assert(bool predicate)
        {
            if (!predicate)
            {
                string message = "Detected an assertion failure.";
                this.Scheduler.NotifyAssertionFailure(message);
            }
        }

        /// <summary>
        /// Checks if the assertion holds, and if not it throws an
        /// <see cref="AssertionFailureException"/> exception.
        /// </summary>
        /// <param name="predicate">Predicate</param>
        /// <param name="s">Message</param>
        /// <param name="args">Message arguments</param>
        public override void Assert(bool predicate, string s, params object[] args)
        {
            if (!predicate)
            {
                string message = IO.Utilities.Format(s, args);
                this.Scheduler.NotifyAssertionFailure(message);
            }
        }

        /// <summary>
        /// Checks that the specified machine method was invoked properly.
        /// </summary>
        /// <param name="caller">The caller machine.</param>
        /// <param name="method">The invoked machine method.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual void CheckMachineMethodInvocation(BaseMachine caller, string method)
        {
            // Override to implement the notification.
        }

        #endregion

        #region nondeterministic choices

        /// <summary>
        /// Returns a nondeterministic boolean choice, that can be
        /// controlled during analysis or testing.
        /// </summary>
        /// <param name="caller">The caller machine.</param>
        /// <param name="maxValue">The max value.</param>
        /// <returns>Boolean</returns>
        protected internal override bool GetNondeterministicBooleanChoice(BaseMachine caller, int maxValue)
        {
            this.CheckMachineMethodInvocation(caller, MachineApiNames.RandomApiName);

            if (caller != null)
            {
                caller.Info.ProgramCounter++;
            }

            var choice = this.Scheduler.GetNextNondeterministicBooleanChoice(maxValue);
            this.Logger.OnRandom(caller?.Id, choice);
            this.BugTrace.AddRandomChoiceStep(caller?.Id, caller?.CurrentStateName ?? String.Empty, choice);
            return choice;
        }

        /// <summary>
        /// Returns a fair nondeterministic boolean choice, that can be
        /// controlled during analysis or testing.
        /// </summary>
        /// <param name="caller">The caller machine.</param>
        /// <param name="uniqueId">Unique id</param>
        /// <returns>Boolean</returns>
        protected internal override bool GetFairNondeterministicBooleanChoice(BaseMachine caller, string uniqueId)
        {
            this.CheckMachineMethodInvocation(caller, MachineApiNames.FairRandomApiName);

            if (caller != null)
            {
                caller.Info.ProgramCounter++;
            }

            var choice = this.Scheduler.GetNextNondeterministicBooleanChoice(2, uniqueId);
            this.Logger.OnRandom(caller?.Id, choice);
            this.BugTrace.AddRandomChoiceStep(caller?.Id, caller?.CurrentStateName ?? String.Empty, choice);
            return choice;
        }

        /// <summary>
        /// Returns a nondeterministic integer choice, that can be
        /// controlled during analysis or testing.
        /// </summary>
        /// <param name="caller">The caller machine.</param>
        /// <param name="maxValue">The max value.</param>
        /// <returns>Integer</returns>
        protected internal override int GetNondeterministicIntegerChoice(BaseMachine caller, int maxValue)
        {
            this.CheckMachineMethodInvocation(caller, MachineApiNames.RandomIntegerApiName);

            var choice = this.Scheduler.GetNextNondeterministicIntegerChoice(maxValue);
            this.Logger.OnRandom(caller?.Id, choice);
            this.BugTrace.AddRandomChoiceStep(caller?.Id, caller?.CurrentStateName ?? String.Empty, choice);
            return choice;
        }

        #endregion

        #region notifications

        /// <summary>
        /// Notifies that a machine entered a state.
        /// </summary>
        /// <param name="machine">The machine.</param>
        protected internal override void NotifyEnteredState(BaseMachine machine)
        {
            string machineState = machine.CurrentStateName;
            this.BugTrace.AddGotoStateStep(machine.Id, machineState);
            this.Logger.OnMachineState(machine.Id, machineState, isEntry: true);
        }

        /// <summary>
        /// Notifies that a monitor entered a state.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        protected internal override void NotifyEnteredState(Monitor monitor)
        {
            string monitorState = monitor.CurrentStateNameWithTemperature;
            this.BugTrace.AddGotoStateStep(monitor.Id, monitorState);
            this.Logger.OnMonitorState(monitor.GetType().Name, monitor.Id, monitorState, true, monitor.GetHotState());
        }

        /// <summary>
        /// Notifies that a machine exited a state.
        /// </summary>
        /// <param name="machine">The machine.</param>
        protected internal override void NotifyExitedState(BaseMachine machine)
        {
            this.Logger.OnMachineState(machine.Id, machine.CurrentStateName, isEntry: false);
        }

        /// <summary>
        /// Notifies that a monitor exited a state.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        protected internal override void NotifyExitedState(Monitor monitor)
        {
            string monitorState = monitor.CurrentStateNameWithTemperature;
            this.Logger.OnMonitorState(monitor.GetType().Name, monitor.Id, monitorState, false, monitor.GetHotState());
        }

        /// <summary>
        /// Notifies that a machine invoked an action.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="action">Action</param>
        /// <param name="receivedEvent">Event</param>
        protected internal override void NotifyInvokedAction(BaseMachine machine, MethodInfo action, Event receivedEvent)
        {
            string machineState = machine.CurrentStateName;
            this.BugTrace.AddInvokeActionStep(machine.Id, machineState, action);

            this.Logger.OnMachineAction(machine.Id, machineState, action.Name);
            if (this.Configuration.EnableDataRaceDetection)
            {
                Reporter.InAction[machine.Id.Value] = true;
            }
        }

        /// <summary>
        /// Notifies that a machine completed an action.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="action">Action</param>
        /// <param name="receivedEvent">Event</param>
        protected internal override void NotifyCompletedAction(BaseMachine machine, MethodInfo action, Event receivedEvent)
        {
            if (this.Configuration.EnableDataRaceDetection)
            {
                Reporter.InAction[machine.Id.Value] = false;
            }
        }

        /// <summary>
        /// Notifies that a monitor invoked an action.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="action">Action</param>
        /// <param name="receivedEvent">Event</param>
        protected internal override void NotifyInvokedAction(Monitor monitor, MethodInfo action, Event receivedEvent)
        {
            string monitorState = monitor.CurrentStateName;
            this.BugTrace.AddInvokeActionStep(monitor.Id, monitorState, action);
            this.Logger.OnMonitorAction(monitor.GetType().Name, monitor.Id, action.Name, monitorState);
        }

        /// <summary>
        /// Notifies that a machine raised an <see cref="Event"/>.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="eventInfo">The event metadata.</param>
        protected internal override void NotifyRaisedEvent(BaseMachine machine, EventInfo eventInfo)
        {
            this.CheckMachineMethodInvocation(machine, MachineApiNames.RaiseEventApiName);
            eventInfo.SetOperationGroupId(this.GetNewOperationGroupId(machine, null));

            string machineState = machine.CurrentStateName;
            this.BugTrace.AddRaiseEventStep(machine.Id, machineState, eventInfo);

            this.Logger.OnMachineEvent(machine.Id, machineState, eventInfo.EventName);
        }

        /// <summary>
        /// Notifies that a monitor raised an <see cref="Event"/>.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="eventInfo">The event metadata.</param>
        protected internal override void NotifyRaisedEvent(Monitor monitor, EventInfo eventInfo)
        {
            string monitorState = monitor.CurrentStateName;
            this.BugTrace.AddRaiseEventStep(monitor.Id, monitorState, eventInfo);

            this.Logger.OnMonitorEvent(monitor.GetType().Name, monitor.Id, monitor.CurrentStateName,
                eventInfo.EventName, isProcessing: false);
        }

        /// <summary>
        /// Notifies that a machine is handling a raised event.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="eventInfo">The event metadata.</param>
        protected internal override void NotifyHandleRaisedEvent(BaseMachine machine, EventInfo eventInfo)
        {
            if (this.Configuration.ReportActivityCoverage)
            {
                this.ReportActivityCoverageOfStateTransition(machine, eventInfo);
            }
        }

        /// <summary>
        /// Notifies that a machine dequeued an event.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="eventInfo">The event metadata.</param>
        protected internal override void NotifyDequeuedEvent(BaseMachine machine, EventInfo eventInfo)
        {
            // The machine inherits the operation group id of the dequeued event.
            machine.Info.OperationGroupId = eventInfo.OperationGroupId;

            // Skip `Receive` if the last operation exited the previous event handler,
            // to avoid scheduling duplicate `Receive` operations.
            if ((machine.Info as SchedulableInfo).SkipNextReceiveSchedulingPoint)
            {
                (machine.Info as SchedulableInfo).SkipNextReceiveSchedulingPoint = false;
            }
            else
            {
                (machine.Info as SchedulableInfo).NextOperationMatchingSendIndex = (ulong)eventInfo.SendStep;
                this.Scheduler.Schedule(OperationType.Receive, OperationTargetType.Inbox, machine.Info.Id);
            }

            this.Logger.OnDequeue(machine.Id, machine.CurrentStateName, eventInfo.EventName);

            if (this.Configuration.EnableDataRaceDetection)
            {
                Reporter.RegisterDequeue(eventInfo.OriginInfo?.SenderMachineId, machine.Id, eventInfo.Event,
                    (ulong)eventInfo.SendStep);
            }

            this.BugTrace.AddDequeueEventStep(machine.Id, machine.CurrentStateName, eventInfo);

            if (this.Configuration.ReportActivityCoverage)
            {
                this.ReportActivityCoverageOfReceivedEvent(machine, eventInfo);
                this.ReportActivityCoverageOfStateTransition(machine, eventInfo);
            }
        }

        /// <summary>
        /// Notifies that a machine invoked pop.
        /// </summary>
        /// <param name="machine">The machine.</param>
        protected internal override void NotifyPop(BaseMachine machine)
        {
            this.CheckMachineMethodInvocation(machine, "Pop");
            this.Logger.OnPop(machine.Id, String.Empty, machine.CurrentStateName);
            if (this.Configuration.ReportActivityCoverage)
            {
                this.ReportActivityCoverageOfPopTransition(machine, machine.CurrentState, machine.GetStateTypeAtStackIndex(1));
            }
        }

        /// <summary>
        /// Notifies that a machine has halted.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="inbox">The machine inbox.</param>
        protected internal override void NotifyHalted(BaseMachine machine, LinkedList<EventInfo> inbox)
        {
            var mustHandleEvent = inbox.FirstOrDefault(ev => ev.MustHandle);
            this.Assert(mustHandleEvent == null,
                "Machine '{0}' halted before dequeueing must-handle event '{1}'.\n",
                machine.Id, mustHandleEvent?.EventName ?? String.Empty);

            this.BugTrace.AddHaltStep(machine.Id, null);
            this.Logger.OnHalt(machine.Id, inbox.Count);
            this.MachineMap.TryRemove(machine.Id, out machine);
        }

        /// <summary>
        /// Notifies that the inbox of the specified machine is about to be
        /// checked to see if the default event handler should fire.
        /// </summary>
        protected internal override void NotifyDefaultEventHandlerCheck(BaseMachine machine)
        {
            this.Scheduler.Schedule(OperationType.Send, OperationTargetType.Inbox, machine.Info.Id);
            // If the default event handler fires, the next receive in NotifyDefaultHandlerFired
            // will use this as its NextOperationMatchingSendIndex.
            // If it does not fire, NextOperationMatchingSendIndex will be overwritten.
            (machine.Info as SchedulableInfo).NextOperationMatchingSendIndex = (ulong)this.Scheduler.ScheduledSteps;
        }

        /// <summary>
        /// Notifies that the default handler of the specified machine has been fired.
        /// </summary>
        /// <param name="machine">The machine.</param>
        protected internal override void NotifyDefaultHandlerFired(BaseMachine machine)
        {
            // NextOperationMatchingSendIndex is set in NotifyDefaultEventHandlerCheck.
            this.Scheduler.Schedule(OperationType.Receive, OperationTargetType.Inbox, machine.Info.Id);
        }

        #endregion

        #region code coverage

        /// <summary>
        /// Reports coverage for the specified received event.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="eventInfo">The event metadata.</param>
        protected void ReportActivityCoverageOfReceivedEvent(BaseMachine machine, EventInfo eventInfo)
        {
            string originMachine = eventInfo.OriginInfo.SenderMachineName;
            string originState = eventInfo.OriginInfo.SenderStateName;
            string edgeLabel = eventInfo.EventType.Name;
            string destMachine = machine.GetType().Name;
            string destState = StateGroup.GetQualifiedStateName(machine.CurrentState);
            this.CoverageInfo.AddTransition(originMachine, originState, edgeLabel, destMachine, destState);
        }

        /// <summary>
        /// Reports coverage for the specified monitor event.
        /// </summary>
        /// <param name="sender">The sender machine.</param>
        /// <param name="monitor">The monitor.</param>
        /// <param name="e">Event</param>
        private void ReportActivityCoverageOfMonitorEvent(BaseMachine sender, Monitor monitor, Event e)
        {
            string originMachine = (sender == null) ? "Env" : sender.GetType().Name;
            string originState = (sender == null) ? "Env" : ((sender.CurrentState == null) ? "None" :
                StateGroup.GetQualifiedStateName(sender.CurrentState));
            string edgeLabel = e.GetType().Name;
            string destMachine = monitor.GetType().Name;
            string destState = StateGroup.GetQualifiedStateName(monitor.CurrentState);
            this.CoverageInfo.AddTransition(originMachine, originState, edgeLabel, destMachine, destState);
        }

        /// <summary>
        /// Reports coverage for the specified machine.
        /// </summary>
        /// <param name="machine">The machine.</param>
        private void ReportActivityCoverageOfMachine(BaseMachine machine)
        {
            var machineName = machine.GetType().Name;

            // fetch states
            var states = machine.GetAllStates();

            foreach (var state in states)
            {
                this.CoverageInfo.DeclareMachineState(machineName, state);
            }

            // fetch registered events
            var pairs = machine.GetAllStateEventPairs();

            foreach (var tup in pairs)
            {
                this.CoverageInfo.DeclareStateEvent(machineName, tup.Item1, tup.Item2);
            }
        }

        /// <summary>
        /// Reports coverage for the specified monitor.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        private void ReportActivityCoverageOfMonitor(Monitor monitor)
        {
            var monitorName = monitor.GetType().Name;

            // fetch states
            var states = monitor.GetAllStates();

            foreach (var state in states)
            {
                this.CoverageInfo.DeclareMachineState(monitorName, state);
            }

            // fetch registered events
            var pairs = monitor.GetAllStateEventPairs();

            foreach (var tup in pairs)
            {
                this.CoverageInfo.DeclareStateEvent(monitorName, tup.Item1, tup.Item2);
            }
        }

        /// <summary>
        /// Reports coverage for the specified state transition.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="eventInfo">The event metadata.</param>
        private void ReportActivityCoverageOfStateTransition(BaseMachine machine, EventInfo eventInfo)
        {
            string originMachine = machine.GetType().Name;
            string originState = StateGroup.GetQualifiedStateName(machine.CurrentState);
            string destMachine = machine.GetType().Name;

            string edgeLabel = String.Empty;
            string destState = String.Empty;
            if (eventInfo.Event is GotoStateEvent)
            {
                edgeLabel = "goto";
                destState = StateGroup.GetQualifiedStateName((eventInfo.Event as GotoStateEvent).State);
            }
            else if (eventInfo.Event is PushStateEvent)
            {
                edgeLabel = "push";
                destState = StateGroup.GetQualifiedStateName((eventInfo.Event as PushStateEvent).State);
            }
            else if (machine.GotoTransitions.ContainsKey(eventInfo.EventType))
            {
                edgeLabel = eventInfo.EventType.Name;
                destState = StateGroup.GetQualifiedStateName(
                    machine.GotoTransitions[eventInfo.EventType].TargetState);
            }
            else if (machine.PushTransitions.ContainsKey(eventInfo.EventType))
            {
                edgeLabel = eventInfo.EventType.Name;
                destState = StateGroup.GetQualifiedStateName(
                    machine.PushTransitions[eventInfo.EventType].TargetState);
            }
            else
            {
                return;
            }

            this.CoverageInfo.AddTransition(originMachine, originState, edgeLabel, destMachine, destState);
        }

        /// <summary>
        /// Reports coverage for a pop transition.
        /// </summary>
        /// <param name="machine">The machine.</param>
        /// <param name="fromState">Top of the stack state</param>
        /// <param name="toState">Next to top state of the stack</param>
        private void ReportActivityCoverageOfPopTransition(BaseMachine machine, Type fromState, Type toState)
        {
            string originMachine = machine.GetType().Name;
            string originState = StateGroup.GetQualifiedStateName(fromState);
            string destMachine = machine.GetType().Name;
            string edgeLabel = "pop";
            string destState = StateGroup.GetQualifiedStateName(toState);

            this.CoverageInfo.AddTransition(originMachine, originState, edgeLabel, destMachine, destState);
        }

        /// <summary>
        /// Reports coverage for the specified state transition.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="e">Event</param>
        private void ReportActivityCoverageOfMonitorTransition(Monitor monitor, Event e)
        {
            string originMachine = monitor.GetType().Name;
            string originState = StateGroup.GetQualifiedStateName(monitor.CurrentState);
            string destMachine = originMachine;

            string edgeLabel = String.Empty;
            string destState = String.Empty;
            if (e is GotoStateEvent)
            {
                edgeLabel = "goto";
                destState = StateGroup.GetQualifiedStateName((e as GotoStateEvent).State);
            }
            else if (monitor.GotoTransitions.ContainsKey(e.GetType()))
            {
                edgeLabel = e.GetType().Name;
                destState = StateGroup.GetQualifiedStateName(
                    monitor.GotoTransitions[e.GetType()].TargetState);
            }
            else
            {
                return;
            }

            this.CoverageInfo.AddTransition(originMachine, originState, edgeLabel, destMachine, destState);
        }

        #endregion

        #region utilities

        /// <summary>
        /// Returns the fingerprint of the current program state.
        /// </summary>
        /// <returns>Fingerprint</returns>
        internal Fingerprint GetProgramState()
        {
            Fingerprint fingerprint = null;

            unchecked
            {
                int hash = 19;

                foreach (var machine in this.MachineMap.Values.OrderBy(mi => mi.Id.Value))
                {
                    hash = hash * 31 + machine.GetCachedState();
                    hash = hash * 31 + (int)(machine.Info as SchedulableInfo).NextOperationType;
                }

                foreach (var monitor in this.Monitors)
                {
                    hash = hash * 31 + monitor.GetCachedState();
                }

                fingerprint = new Fingerprint(hash);
            }

            return fingerprint;
        }

        #endregion

        #region logging

        /// <summary>
        /// Logs the specified text.
        /// </summary>
        /// <param name="format">Text</param>
        /// <param name="args">Arguments</param>
        protected internal override void Log(string format, params object[] args)
        {
            this.Logger.WriteLine(format, args);
        }

        #endregion

        #region exceptions

        /// <summary>
        /// Throws an <see cref="AssertionFailureException"/> exception
        /// containing the specified exception.
        /// </summary>
        /// <param name="exception">Exception</param>
        /// <param name="s">Message</param>
        /// <param name="args">Message arguments</param>
        protected internal override void WrapAndThrowException(Exception exception, string s, params object[] args)
        {
            string message = IO.Utilities.Format(s, args);
            this.Scheduler.NotifyAssertionFailure(message);
        }

        #endregion

        #region cleanup

        /// <summary>
        /// Disposes runtime resources.
        /// </summary>
        public override void Dispose()
        {
            this.Monitors.Clear();
            this.TaskMap.Clear();
            base.Dispose();
        }

        #endregion
    }
}
