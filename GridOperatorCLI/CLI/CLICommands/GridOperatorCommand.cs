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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator.CommandLine
{

    /// <summary>
    /// The grid operator this node is: its identification, its name, the public
    /// halves of its keys - and, with "party", what the operator of a roaming
    /// network needs to add it, as one line of JSON to hand over.
    /// </summary>
    public class GridOperatorCommand(GridOperatorCLI CLI) : ACLICommand<GridOperatorCLI>(CLI),
                                                           ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public static readonly String CommandName = nameof(GridOperatorCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            if (Arguments.Length == 2 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase) &&
                "party".StartsWith(Arguments[1], StringComparison.OrdinalIgnoreCase))
                return [ SuggestionResponse.ParameterCompleted($"{CommandName} party") ];

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            if (!cli.MayDo(Permission.Read(GridOperatorAccess.POI), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            var own     = cli.GridOperator.Own;
            var entity  = own.Entity;

            if (Arguments.Length == 2 && Arguments[1].Equals("party", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<String[]>([
                           $"What the operator of a roaming network adds the grid operator '{own.Id}' with - its public keys only:",
                           own.PartyJSON().ToString(Formatting.None)
                       ]);

            if (Arguments.Length > 1)
                return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

            var name = entity.Name?.FirstText() ?? "";

            return Task.FromResult<String[]>([
                       $"This node is the grid operator '{own.Id}'{(name.Length > 0 ? $", {name}" : "")}, since {entity.Created.ToISO8601()}.",
                       $"  signing key   {own.SigningKey. Id}  {own.SigningKey. ToJSON()["key"]}",
                       $"  identity key  {own.IdentityKey.Id}  {own.IdentityKey.ToJSON()["key"]}",
                       $"  its private keys are kept in '{own.Directory}'",
                       $"  {entity.GridConnectionPoints.Count} grid connection point(s); 'gridConnectionPoints' lists them",
                       cli.GridOperator.RoamingNetworkSettings.IsSet
                           ? $"  it publishes itself to {cli.GridOperator.RoamingNetworkSettings.URL}; 'publish' says what that would change"
                           : $"  it publishes itself to no roaming network; '{CommandName} party' is what its operator needs to add it"
                   ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [party] - the grid operator this node is, and its public keys; with 'party' what the operator of a roaming network adds it with";

        #endregion

    }

}
