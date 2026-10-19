/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of GridOperatorCLI <https://github.com/OpenChargingCloud/GridOperatorCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Reflection;

using org.GraphDefined.Vanaheimr.CLI;

using cloud.charging.open.protocols.WWCP.Node.CommandLine;

using GO = cloud.charging.open.GridOperator.GridOperator;

#endregion

namespace cloud.charging.open.GridOperator.CommandLine
{

    /// <summary>
    /// The command line of a running grid operator.
    /// </summary>
    /// <remarks>
    /// Everything a command needs is reachable from here: the grid operator
    /// itself, and through it its grid connection points, its local
    /// controllers, its limits and everything the JSON API can do. A command
    /// is a third way of asking for the same thing, beside the web interface
    /// and the JSON API - never an implementation of its own.
    ///
    /// Commands are not listed anywhere. The commands every node has are the
    /// node's, and NodeCLI, which this derives from, finds them in WWCP_Node.
    /// The constructor registers this type as well, so that this assembly is
    /// walked for anything that implements ICLICommand and can be built from
    /// a GridOperatorCLI: a command of the grid operator's own is a new file
    /// below CLI/CLICommands and nothing else.
    /// </remarks>
    public class GridOperatorCLI : NodeCLI
    {

        #region Properties

        /// <summary>
        /// The grid operator these commands are about.
        /// </summary>
        public GO      GridOperator  { get; }

        /// <summary>
        /// Who asks, as the grid operator writes it into its log: the account
        /// signed in over SSH, or the console.
        /// </summary>
        public String  Who
            => Caller.Account?.Id.ToString() ?? "the console";

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create the command line of the given grid operator.
        /// </summary>
        /// <param name="GridOperator">The running grid operator.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one and WWCP_Node are searched either way.</param>
        public GridOperatorCLI(GO                 GridOperator,
                               params Assembly[]  AssembliesWithCLICommands)

            : base(GridOperator, AssembliesWithCLICommands)

        {

            this.GridOperator = GridOperator;

            RegisterCLIType(typeof(GridOperatorCLI));

        }

        /// <summary>
        /// Create the command line of the given grid operator on the given
        /// terminal, for the given caller - a session over SSH.
        /// </summary>
        /// <param name="GridOperator">The running grid operator.</param>
        /// <param name="Terminal">What the command line is typed at and written on.</param>
        /// <param name="Caller">Who is typing at it.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one and the node's are searched either way.</param>
        public GridOperatorCLI(GO                 GridOperator,
                               ICLITerminal       Terminal,
                               CLICaller          Caller,
                               params Assembly[]  AssembliesWithCLICommands)

            : base(GridOperator, Terminal, Caller, AssembliesWithCLICommands)

        {

            this.GridOperator = GridOperator;

            RegisterCLIType(typeof(GridOperatorCLI));

        }

        #endregion


        #region (protected override) GetPrompt()

        /// <summary>
        /// Which grid operator this is and which port it answers on, because a
        /// machine that is one of several on a bench should say which one it is
        /// before it asks for a command.
        /// </summary>
        protected override String GetPrompt()

            => $"{GridOperator.Own.Id}:{GridOperator.HTTPPort}> ";

        #endregion

    }

}
