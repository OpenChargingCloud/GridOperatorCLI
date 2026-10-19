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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator.CommandLine
{

    /// <summary>
    /// The local controllers that may sign in at the OpenADR 3 VTN - the VENs
    /// of the charging pools on this grid: all of them, one line each; "add"
    /// one, which says its secret the one time; "secret" makes a new one;
    /// "remove" one.
    /// </summary>
    public class LocalControllersCommand(GridOperatorCLI CLI) : ACLICommand<GridOperatorCLI>(CLI),
                                                               ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public static readonly String CommandName = nameof(LocalControllersCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            if (Arguments.Length == 2 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase))
                return new[] { "add", "secret", "remove" }.
                           Where (word => word.StartsWith(Arguments[1], StringComparison.OrdinalIgnoreCase)).
                           Select(word => SuggestionResponse.ParameterCompleted($"{CommandName} {word}"));

            if (Arguments.Length == 3 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase) &&
                (Arguments[1].Equals("secret", StringComparison.OrdinalIgnoreCase) || Arguments[1].Equals("remove", StringComparison.OrdinalIgnoreCase)))
                return cli.GridOperator.OpenADR.Logins().
                           Select(login => login.VENName).
                           Where (name  => name.StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase)).
                           Select(name  => SuggestionResponse.ParameterCompleted($"{CommandName} {Arguments[1]} {name}"));

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            var rest = Arguments.Skip(1).ToArray();

            if (rest.Length > 0)
            {

                var what = rest[0].ToLowerInvariant();

                if (what is "add" or "secret" or "remove")
                {

                    if (!cli.MayDo(Permission.Edit(GridOperatorAccess.OpenADR), $"{CommandName} {what}", out var notAllowed))
                        return Task.FromResult<String[]>([ notAllowed ]);

                    return Task.FromResult(what switch {
                        "add"     => Added  (rest[1..]),
                        "secret"  => Renewed(rest[1..]),
                        _         => Removed(rest[1..])
                    });

                }

                return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

            }

            if (!cli.MayDo(Permission.Read(GridOperatorAccess.OpenADR), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            var logins = cli.GridOperator.OpenADR.Logins();

            if (logins.Count == 0)
                return Task.FromResult<String[]>([ $"No local controller may sign in at {cli.GridOperator.OpenADRVTNURL} yet: '{CommandName} add <venName> <chargingPoolId> <gridConnectionPointId>' lets one." ]);

            var width = logins.Max(login => login.VENName.Length);

            return Task.FromResult<String[]>([
                       .. logins.Select(login => {
                              var seen = cli.GridOperator.OpenADR.LastSeen(login.VENName);
                              return $"{login.VENName.PadRight(width)}  pool {login.ChargingPoolId}, behind {login.GridConnectionPointId}, " +
                                     (seen.HasValue ? $"last seen {seen.Value.ToLocalTime():HH:mm:ss}" : "not seen since this start");
                          }),
                       $"They sign in at {cli.GridOperator.OpenADRTokenURL}."
                   ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [add <venName> <chargingPoolId> <gridConnectionPointId> | secret <venName> | remove <venName>] - the local controllers that may sign in at the OpenADR 3 VTN";

        #endregion


        #region (private) Added(Arguments) / Renewed(Arguments) / Removed(Arguments)

        private String[] Added(String[] Arguments)
        {

            if (Arguments.Length < 3)
                return [ $"Usage: {CommandName} add <venName> <chargingPoolId> <gridConnectionPointId> [<description>]" ];

            var json = new JObject(
                           new JProperty("venName",                Arguments[0]),
                           new JProperty("chargingPoolId",         Arguments[1]),
                           new JProperty("gridConnectionPointId",  Arguments[2])
                       );

            if (Arguments.Length > 3)
                json["description"] = String.Join(" ", Arguments[3..]);

            if (!cli.GridOperator.TryAddLocalController(json, cli.Who, out var login, out var secret, out var error, out _))
                return [ error ];

            return Credentials($"The local controller '{login.VENName}' may sign in now. Its secret is said this once:", login.VENName, secret);

        }

        private String[] Renewed(String[] Arguments)
        {

            if (Arguments.Length != 1)
                return [ $"Usage: {CommandName} secret <venName>" ];

            if (!cli.GridOperator.TryNewSecret(Arguments[0], cli.Who, out var login, out var secret, out var error, out _))
                return [ error ];

            return Credentials($"The local controller '{login.VENName}' has a new secret, said this once; the old one signs in no more:", login.VENName, secret);

        }

        private String[] Removed(String[] Arguments)
        {

            if (Arguments.Length != 1)
                return [ $"Usage: {CommandName} remove <venName>" ];

            return cli.GridOperator.TryRemoveLocalController(Arguments[0], cli.Who, out var error, out _)
                       ? [ $"The local controller '{Arguments[0]}' signs in no more." ]
                       : [ error ];

        }

        private String[] Credentials(String Sentence, String ClientId, String Secret)

            => [
                   Sentence,
                   $"  token URL      {cli.GridOperator.OpenADRTokenURL}",
                   $"  client ID      {ClientId}",
                   $"  client secret  {Secret}"
               ];

        #endregion

    }

}
