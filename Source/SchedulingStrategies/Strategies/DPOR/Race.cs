﻿// ------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See License.txt in the repo root for full license information.
// ------------------------------------------------------------------------------------------------

namespace Microsoft.PSharp.TestingServices.SchedulingStrategies.DPOR
{
    /// <summary>
    /// Represents a race (two visible operation that are concurrent but dependent)
    /// that can be reversed to reach a different terminal state.
    /// </summary>
    internal class Race
    {
        /// <summary>
        /// The index of the first racing visible operation.
        /// </summary>
        public int A;

        /// <summary>
        /// The index of the second racing visible operation.
        /// </summary>
        public int B;

        /// <summary>
        /// Construct a race.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        public Race(int a, int b)
        {
            A = a;
            B = b;
        }
    }
}