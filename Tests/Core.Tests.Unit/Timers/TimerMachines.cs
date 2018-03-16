﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PSharp.Timers;
using Microsoft.PSharp;

namespace Microsoft.PSharp.Core.Tests.Unit
{
	#region internal events and classes

	internal class NonMachineSubClass { }

	internal class Configure : Event
	{
		public TaskCompletionSource<bool> TCS;
		public bool periodic;

		public Configure(TaskCompletionSource<bool> tcs, bool periodic)
		{
			this.TCS = tcs;
			this.periodic = periodic;
		}
	}

	internal class ConfigureWithPeriod : Event
	{
		public TaskCompletionSource<bool> TCS;
		public int period;

		public ConfigureWithPeriod(TaskCompletionSource<bool> tcs, int period)
		{
			this.TCS = tcs;
			this.period = period;
		}
	}

	internal class Marker : Event { }

	internal class TransferTimerAndTCS : Event
	{
		public TimerId tid;
		public TaskCompletionSource<bool> TCS;

		public TransferTimerAndTCS(TimerId tid, TaskCompletionSource<bool> TCS)
		{
			this.tid = tid;
			this.TCS = TCS;
		}
	}

	#endregion

	#region timer machines

	#region check basic StartTimer/StopTimer
	class T1 : TMachine
	{
		#region fields
		TimerId tid;
		object payload = new object();
		TaskCompletionSource<bool> tcs;
		int count;
		bool periodic;

		#endregion
		[Start]
		[OnEntry(nameof(InitOnEntry))]
		[OnEventDoAction(typeof(TimerElapsedEvent), nameof(HandleTimeout))]
		class Init : MachineState { }

		void InitOnEntry()
		{
			Configure e = (this.ReceivedEvent as Configure);
			tcs = e.TCS;
			periodic = e.periodic;
			count = 0;

			if (periodic)
			{
				// Start a periodic timer with 10ms timeouts
				tid = StartTimer(payload, true, 10);
			}
			else
			{
				// Start a one-off timer 
				tid = StartTimer(payload, false, 10);
			}
		}

		void HandleTimeout()
		{
			count++;

			// for testing single timeout
			if (!periodic)
			{
				if (count == 1)
				{
					StopTimer(tid, true).Wait();
					try
					{
						this.Assert(false);
					}
					finally
					{
						tcs.SetResult(true);
						this.Raise(new Halt());
					}
				}
			}

			// for testing periodic timeouts
			else
			{
				if (count == 100)
				{
					StopTimer(tid, true).Wait();
					try
					{
						this.Assert(false);
					}
					finally
					{
						tcs.SetResult(true);
						this.Raise(new Halt());
					}
				}
			}
		}
	}
	#endregion

	#region check flushing
	class FlushingClient : TMachine
	{
		#region fields
		/// <summary>
		/// A dummy payload object received with timeout events.
		/// </summary>
		object payload = new object();

		/// <summary>
		/// Timer used in the Ping State.
		/// </summary>
		TimerId pingTimer;

		/// <summary>
		/// Timer used in the Pong state.
		/// </summary>
		TimerId pongTimer;

		TaskCompletionSource<bool> tcs;

		#endregion

		#region states

		/// <summary>
		/// Start the pingTimer and start handling the timeout events from it.
		/// After handling 10 events, stop pingTimer and move to the Pong state.
		/// </summary>
		[Start]
		[OnEntry(nameof(DoPing))]
		class Ping : MachineState { }

		/// <summary>
		/// Start the pongTimer and start handling the timeout events from it.
		/// After handling 10 events, stop pongTimer and move to the Ping state.
		/// </summary>
		[OnEntry(nameof(DoPong))]
		[OnEventDoAction(typeof(TimerElapsedEvent), nameof(HandleTimeoutForPong))]
		class Pong : MachineState { }
		#endregion

		#region event handlers

		private void DoPing()
		{
			tcs = (this.ReceivedEvent as Configure).TCS;
	
			// Start a periodic timer with timeout interval of 1sec.
			// The timer generates TimerElapsedEvent with 'm' as payload.
			pingTimer = StartTimer(payload, true, 100);
			this.StopTimer(pingTimer, true).Wait();
			this.Goto<Pong>();
		}

		/// <summary>
		/// Handle timeout events from the pongTimer.
		/// </summary>
		private void DoPong()
		{
			// Start a periodic timer with timeout interval of 0.5sec.
			// The timer generates TimerElapsedEvent with 'm' as payload.
			pongTimer = StartTimer(payload, false, 50);
		}

		private void HandleTimeoutForPong()
		{
			TimerElapsedEvent e = (this.ReceivedEvent as TimerElapsedEvent);

			if(e.Tid == this.pongTimer)
			{
				try
				{
					this.Assert(false);
				}
				finally
				{
					tcs.SetResult(true);
					this.Raise(new Halt());
				}
			}
			else
			{
				tcs.SetResult(true);
				this.Raise(new Halt());
			}
			
		}
		#endregion
	}
	#endregion

	#region check illegal timer stoppage
	
	class T2 : TMachine
	{
		#region fields

		TimerId tid;
		TaskCompletionSource<bool> tcs;
		object payload = new object();
		MachineId m;

		#endregion
		[Start]
		[OnEntry(nameof(Initialize))]
		[IgnoreEvents(typeof(TimerElapsedEvent))]
		class Init : MachineState { }
		#region states

		#endregion

		#region handlers
		void Initialize()
		{
			tcs = (this.ReceivedEvent as Configure).TCS;
			tid = this.StartTimer(this.payload, true, 100);
			m = CreateMachine(typeof(T3), new TransferTimerAndTCS(tid, tcs));
			this.Raise(new Halt());
		}

		#endregion


	}

	class T3 : TMachine
	{
		#region states

		[Start]
		[OnEntry(nameof(Initialize))]
		class Init : MachineState { }

		#endregion

		#region handlers

		void Initialize()
		{
			TimerId tid = (this.ReceivedEvent as TransferTimerAndTCS).tid;
			TaskCompletionSource<bool> tcs = (this.ReceivedEvent as TransferTimerAndTCS).TCS;

			try
			{
				// trying to stop a timer created by a different machine. 
				// should throw an assertion violation
				this.StopTimer(tid, true).Wait();
			}
			finally
			{
				tcs.SetResult(true);
				this.Raise(new Halt());
			}
		}

		#endregion


	}
	#endregion

	#region check illegal period specification
	class T4 : TMachine
	{
		#region fields

		object payload = new object();

		#endregion

		#region states
		[Start]
		[OnEntry(nameof(Initialize))]
		class Init : MachineState { }
		#endregion

		#region handlers
		void Initialize()
		{
			var tcs = (this.ReceivedEvent as ConfigureWithPeriod).TCS;
			var period = (this.ReceivedEvent as ConfigureWithPeriod).period;

			try
			{
				this.StartTimer(this.payload, true, period);
			}
			finally
			{
				tcs.SetResult(true);
				this.Raise(new Halt());
			}
		}
		#endregion
	}
	#endregion


	#endregion
}