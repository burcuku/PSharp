﻿//-----------------------------------------------------------------------
// <copyright file="SharedRegisterEvent.cs">
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

namespace Microsoft.PSharp.SharedObjects
{
    /// <summary>
    /// Event used to communicate with a shared register machine.
    /// </summary>
    internal class SharedRegisterEvent: Event 
    {
        /// <summary>
        /// Supported shared register operations.
        /// </summary>
        internal enum SharedRegisterOperation { GET, SET, UPDATE };

        /// <summary>
        /// The operation stored in this event.
        /// </summary>
        public SharedRegisterOperation Operation { get; private set; }

        /// <summary>
        /// The shared register value stored in this event.
        /// </summary>
        public object Value { get; private set; }

        /// <summary>
        /// The shared register func stored in this event.
        /// </summary>
        public object Func { get; private set; }

        /// <summary>
        /// The sender machine stored in this event.
        /// </summary>
        public MachineId Sender { get; private set; }

        /// <summary>
        /// Creates a new event with the specified operation.
        /// </summary>
        /// <param name="op">SharedRegisterOperation</param>
        /// <param name="value">Value</param>
        /// <param name="func">Func</param>
        /// <param name="sender">Sender</param>
        SharedRegisterEvent(SharedRegisterOperation op, object value, object func, MachineId sender)
        {
            Operation = op;
            Value = value;
            Func = func;
            Sender = sender;
        }

        /// <summary>
        /// Creates a new event for the 'UPDATE' operation.
        /// </summary>
        /// <param name="func">Func</param>
        /// <param name="sender">Sender</param>
        /// <returns>SharedRegisterEvent</returns>
        public static SharedRegisterEvent UpdateEvent(object func, MachineId sender)
        {
            return new SharedRegisterEvent(SharedRegisterOperation.UPDATE, null, func, sender);
        }

        /// <summary>
        /// Creates a new event for the 'SET' operation.
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>SharedRegisterEvent</returns>
        public static SharedRegisterEvent SetEvent(object value)
        {
            return new SharedRegisterEvent(SharedRegisterOperation.SET, value, null, null);
        }

        /// <summary>
        /// Creates a new event for the 'GET' operation.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <returns>SharedRegisterEvent</returns>
        public static SharedRegisterEvent GetEvent(MachineId sender)
        {
            return new SharedRegisterEvent(SharedRegisterOperation.GET, null, null, sender);
        }
    }
}
