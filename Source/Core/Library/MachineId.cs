﻿//-----------------------------------------------------------------------
// <copyright file="MachineId.cs">
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
using System.Runtime.Serialization;
using System.Threading;

namespace Microsoft.PSharp
{
    /// <summary>
    /// Unique machine id.
    /// </summary>
    [DataContract]
    public sealed class MachineId
    {
        #region fields

        /// <summary>
        /// The P# runtime that executes the machine with this id.
        /// </summary>
        public PSharpRuntime Runtime { get; private set; }

        /// <summary>
        /// Name of the machine.
        /// </summary>
        [DataMember]
        public readonly string Name;

        /// <summary>
        /// Type of the machine with this id.
        /// </summary>
        [DataMember]
        public readonly string Type;

        /// <summary>
        /// Unique id value.
        /// </summary>
        [DataMember]
        public readonly ulong Value;

        /// <summary>
        /// Generation of the runtime that created this machine id.
        /// </summary>
        [DataMember]
        public readonly ulong Generation;

        /// <summary>
        /// Endpoint.
        /// </summary>
        [DataMember]
        public readonly string Endpoint;

        #endregion

        #region constructors

        /// <summary>
        /// Creates a new machine id.
        /// </summary>
        /// <param name="type">Machine type</param>
        /// <param name="friendlyName">Friendly machine name</param>
        /// <param name="runtime">PSharpRuntime</param>
        /// <param name="isBound">Is the id bound to a machine?</param>
        internal MachineId(Type type, string friendlyName, PSharpRuntime runtime, bool isBound)
        {
            Runtime = runtime;
            Endpoint = Runtime.NetworkProvider.GetLocalEndpoint();
            
            // Atomically increments and safely wraps into an unsigned long.
            Value = (ulong)Interlocked.Increment(ref runtime.MachineIdCounter) - 1;

            // Checks for overflow.
            Runtime.Assert(Value != ulong.MaxValue, "Detected MachineId overflow.");

            Generation = runtime.Configuration.RuntimeGeneration;

            Type = type.FullName;
            if (friendlyName != null && friendlyName.Length > 0)
            {
                Name = string.Format("{0}({1})", friendlyName, Value);
            }
            else
            {
                Name = string.Format("{0}({1})", Type, Value);
            }
        }

        /// <summary>
        /// Internal constructor. 
        /// </summary>
        /// <param name="type">Machine type</param>
        /// <param name="name">Machine name</param>
        /// <param name="value">Id value</param>
        /// <param name="generation">Id generation</param>
        internal MachineId(string type, string name, ulong value, ulong generation)
        {
            Type = type;
            Value = value;
            Generation = generation;
            Name = name;
        }

        /// <summary>
        /// Bind the machine id.
        /// </summary>
        /// <param name="runtime">PSharpRuntime</param>
        internal void Bind(PSharpRuntime runtime)
        {
            Runtime = runtime;
        }

        #endregion

        #region generic public and override methods
        
        /// <summary>
        /// Determines whether the specified System.Object is equal
        /// to the current System.Object.
        /// </summary>
        /// <param name="obj">Object</param>
        /// <returns>Boolean</returns>
        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            MachineId mid = obj as MachineId;
            if (mid == null)
            {
                return false;
            }

            return Value == mid.Value && Generation == mid.Generation;
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>int</returns>
        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + Value.GetHashCode();
            hash = hash * 23 + Generation.GetHashCode();
            return hash;
        }

        /// <summary>
        /// Returns a string that represents the current machine id.
        /// </summary>
        /// <returns>string</returns>
        public override string ToString()
        {
            return Name;
        }

        #endregion
    }
}
