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

using System.Globalization;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator.CommandLine
{

    /// <summary>
    /// The limits sent to the local controllers: all of them, one line each;
    /// "send" one to those behind a grid connection point, or to all, from
    /// now; "cancel" one.
    /// </summary>
    public class LimitsCommand(GridOperatorCLI CLI) : ACLICommand<GridOperatorCLI>(CLI),
                                                     ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public static readonly String CommandName = nameof(LimitsCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            if (Arguments.Length == 2 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase))
                return new[] { "send", "cancel" }.
                           Where (word => word.StartsWith(Arguments[1], StringComparison.OrdinalIgnoreCase)).
                           Select(word => SuggestionResponse.ParameterCompleted($"{CommandName} {word}"));

            if (Arguments.Length == 3 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase) &&
                Arguments[1].Equals("send", StringComparison.OrdinalIgnoreCase))
                return cli.GridOperator.Own.Entity.GridConnectionPoints.
                           Select(point => point.Id.ToString()).
                           Append("all").
                           Where (word  => word.StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase)).
                           Select(word  => SuggestionResponse.ParameterCompleted($"{CommandName} send {word}"));

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            var rest = Arguments.Skip(1).ToArray();

            if (rest.Length > 0 && rest[0].Equals("send", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Sent(rest[1..]));

            if (rest.Length > 0 && rest[0].Equals("cancel", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Cancelled(rest[1..]));

            if (rest.Length > 0)
                return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

            if (!cli.MayDo(Permission.Read(GridOperatorAccess.OpenADR), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            var now     = cli.GridOperator.TimeProvider.GetUtcNow();
            var limits  = cli.GridOperator.OpenADR.Events();

            if (limits.Count == 0)
                return Task.FromResult<String[]>([ $"No limit was sent yet: '{CommandName} send <gridConnectionPointId|all> import|export <kW> <minutes>' sends one." ]);

            return Task.FromResult<String[]>([
                       .. limits.Select(limit => $"{limit.Id}  {limit.StateAt(now),-9}  {limit.Kind} {limit.LimitKW.ToString("0.###", CultureInfo.InvariantCulture)} kW  " +
                                                 $"{limit.Start.ToLocalTime():yyyy-MM-dd HH:mm} to {limit.End.ToLocalTime():HH:mm}  " +
                                                 $"{(limit.GridConnectionPointId is not null ? $"behind {limit.GridConnectionPointId}" : "for all")}: " +
                                                 String.Join(", ", limit.VENNames))
                   ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [send <gridConnectionPointId|all> import|export <kW> <minutes> | cancel <id>] - the limits sent to the local controllers over OpenADR 3";

        #endregion


        #region (private) Sent(Arguments) / Cancelled(Arguments)

        private String[] Sent(String[] Arguments)
        {

            if (!cli.MayDo(Permission.Run(GridOperatorAccess.OpenADR), $"{CommandName} send", out var refused))
                return [ refused ];

            if (Arguments.Length != 4 ||
                Arguments[1].ToLowerInvariant() is not ("import" or "export") ||
                !Double.TryParse(Arguments[2], NumberStyles.Float,   CultureInfo.InvariantCulture, out var kW) ||
                !Double.TryParse(Arguments[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                return [ $"Usage: {CommandName} send <gridConnectionPointId|all> import|export <kW> <minutes>" ];

            var json = new JObject(
                           new JProperty("kind",             Arguments[1].Equals("import", StringComparison.OrdinalIgnoreCase) ? "IMPORT_CAPACITY_LIMIT" : "EXPORT_CAPACITY_LIMIT"),
                           new JProperty("limitKW",          kW),
                           new JProperty("durationMinutes",  minutes)
                       );

            if (!Arguments[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                json["gridConnectionPointId"] = Arguments[0];

            return cli.GridOperator.TrySendLimit(json, cli.Who, out var limit, out var error, out _)
                       ? [ $"The limit '{limit.Id}', {limit.Kind} {kW.ToString(CultureInfo.InvariantCulture)} kW for {minutes} minute(s), was sent to {String.Join(", ", limit.VENNames)}." ]
                       : [ error ];

        }

        private String[] Cancelled(String[] Arguments)
        {

            if (!cli.MayDo(Permission.Run(GridOperatorAccess.OpenADR), $"{CommandName} cancel", out var refused))
                return [ refused ];

            if (Arguments.Length != 1)
                return [ $"Usage: {CommandName} cancel <id>" ];

            return cli.GridOperator.TryCancelLimit(Arguments[0], cli.Who, out var error, out _)
                       ? [ $"The limit '{Arguments[0]}' was cancelled: the local controllers do not read it any more." ]
                       : [ error ];

        }

        #endregion

    }

}
