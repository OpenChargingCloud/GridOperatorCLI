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

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

using cloud.charging.open.GridOperator.CommandLine;
using cloud.charging.open.GridOperator.Configuration;
using cloud.charging.open.GridOperator.OpenADR;
using cloud.charging.open.GridOperator.POI;

// Inside this namespace "GridOperator" may be the namespace as well as the
// class, so the class gets a name of its own here.
using GO = cloud.charging.open.GridOperator.GridOperator;

#endregion

namespace cloud.charging.open.GridOperator.CLI
{

    /// <summary>
    /// One grid operator, with its web interface, its OpenADR 3 VTN and a
    /// prompt, until 'quit', Ctrl+C or SIGTERM.
    /// </summary>
    /// <remarks>
    /// What every kind of node's program does is the node's: the switches and
    /// the words -h explains them with, why it could not be set up or could not
    /// start, what goes into the certificate store, the banner and the prompt.
    /// What is left here is the grid operator's: what its configuration holds,
    /// and what its banner says of the grid operator it is, its roaming network
    /// and its local controllers.
    /// </remarks>
    public class Program
    {

        #region (private static) Usage

        /// <summary>
        /// What -h shows: every node's switches, in a grid operator's words.
        /// </summary>
        private static readonly NodeUsage Usage = new (

            Program:            "GridOperatorCLI",
            Kind:               GO.GridOperatorKind,
            DefaultPort:        GO.DefaultHTTPPort,
            FrontendSources:    "libs/GridOperator/GridOperator/Frontend",

            ConfigurationSays:  "where the name servers, the time servers and the roaming network of this grid operator live " +
                               $"(default: {WWCPConfigFile.DefaultFileName} below the repository root). The grid operator this node " +
                                "is gets its signing key and its identity key at the first start, kept below " +
                               $"{OwnGridOperator.DirectoryName}/ beside the file together with its grid connection points - keep a copy " +
                               $"of them somewhere safe; the file's \"{OwnGridOperator.ConfigurationSection}\": {{\"@id\", \"name\"}} says who " +
                               $"it is, read at that first start only, e.g. {{\"@id\": \"DE*SWJ\"}}. \"{RoamingNetworkConfiguration.SectionName}\": " +
                                "{\"url\"} is the roaming network it publishes itself to, e.g. http://127.0.0.1:2356/poi/RNs/QA. " +
                               $"The local controllers that may sign in at its OpenADR 3 VTN, and the limits sent to them, are below " +
                               $"{OpenADRStore.DirectoryName}/.",

            CertificateKinds:   GO.CertificateKinds,

            CertificatesSays:   "tlsIdentity is kept for a roaming hub that asks for a client certificate; nothing presents it yet."

        );

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            // Every node's switches; a grid operator has none of its own.
            var arguments = NodeArguments.Parse(Arguments);

            if (arguments.Refused(Usage) is Int32 refused)
                return refused;

            if (arguments.RefuseTheRest(Usage) is Int32 unknown)
                return unknown;

            var root = NodeProgram.RepositoryRoot("GridOperatorCLI.slnx");

            #endregion

            #region The grid operator

            GO gridOperator;

            try
            {
                gridOperator = new GO(
                                   HTTPHostname:      arguments.HTTPHostname,
                                   HTTPPort:          arguments.Port,
                                   AccountsPath:      arguments.AccountsPathBelow(root),
                                   ConfigFile:        new WWCPConfigFile(arguments.ConfigFilePathBelow(root)),
                                   Frontend:          arguments.Frontend,
                                   CertificatesPath:  arguments.CertificatesPath,
                                   ConsoleLogLevel:   arguments.ConsoleLogLevel,
                                   LogPath:           arguments.LogPathBelow(root),
                                   BridgeDebugLog:    !arguments.NoTrace,
                                   SSH:               arguments.SSH,
                                   Web:               arguments.Web
                               );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(GO.GridOperatorKind, e, arguments.Verbose);
            }

            await using (gridOperator)
            {

                // What somebody signed in over SSH gets: this program's own
                // command line, with its commands beside the node's.
                gridOperator.CommandLines = (terminal, caller) => new GridOperatorCLI(gridOperator, terminal, caller);

                if (gridOperator.ImportCertificates(arguments, out _) is Int32 notImported)
                    return notImported;

                if (arguments.ListCertificates)
                    gridOperator.ListCertificates();

                if (await gridOperator.Started(arguments.Verbose) is Int32 notStarted)
                    return notStarted;

                #region What somebody who just started this needs to know

                var own = gridOperator.Own;

                foreach (var line in gridOperator.Banner(
                                         BesideTheInterfaces: [
                                             ("OpenADR 3 VTN",    gridOperator.OpenADRVTNURL),
                                             ("OpenADR token",    gridOperator.OpenADRTokenURL)
                                         ],
                                         OfTheKind: [
                                             ("grid operator",    $"'{own.Id}'" +
                                                                  (own.Entity.Name?.FirstText() is { Length: > 0 } name ? $", {name}" : "") +
                                                                  (own.IsNew ? ", made now" : "") +
                                                                  $", its keys in {own.Directory}"),
                                             ("grid conn. pts.",  $"{own.Entity.GridConnectionPoints.Count} grid connection point(s)"),
                                             ("roaming network",  gridOperator.RoamingNetworkSettings.IsSet
                                                                      ? gridOperator.RoamingNetworkSettings.URL
                                                                      : "none: publishes nowhere"),
                                             ("local ctrls.",     $"{gridOperator.OpenADR.Logins().Count} may sign in, {gridOperator.OpenADR.Events().Count} limit(s) kept in {gridOperator.OpenADR.FilePath}")
                                         ]))
                    Console.WriteLine(line);

                #endregion

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's: a prompt where somebody can type, and waiting
                // where nobody can, with the log sharing the screen.
                await new GridOperatorCLI(gridOperator).RunUntilStopped();

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
