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
    /// The grid connection points of the grid operator: all of them, one line
    /// each, or one of them as POI 3 writes it; "add" one with its
    /// identification, its voltage level and its contracted import power,
    /// "remove" one.
    /// </summary>
    public class GridConnectionPointsCommand(GridOperatorCLI CLI) : ACLICommand<GridOperatorCLI>(CLI),
                                                                   ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public static readonly String CommandName = nameof(GridConnectionPointsCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            if (Arguments.Length == 2 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase))
                return new[] { "add", "remove" }.
                           Concat(cli.GridOperator.Own.Entity.GridConnectionPoints.Select(point => point.Id.ToString())).
                           Where (word => word.StartsWith(Arguments[1], StringComparison.OrdinalIgnoreCase)).
                           Select(word => SuggestionResponse.ParameterCompleted($"{CommandName} {word}"));

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            var rest = Arguments.Skip(1).ToArray();

            if (rest.Length > 0 && rest[0].Equals("add", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Added(rest[1..]));

            if (rest.Length > 0 && rest[0].Equals("remove", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Removed(rest[1..]));

            if (!cli.MayDo(Permission.Read(GridOperatorAccess.POI), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            if (rest.Length == 1)
                return Task.FromResult<String[]>(
                           cli.GridOperator.TryGetGridConnectionPoint(rest[0], out var point)
                               ? cli.GridOperator.GridConnectionPointJSON(point).ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToArray()
                               : [ $"The grid operator '{cli.GridOperator.Own.Id}' has no grid connection point '{rest[0]}'." ]
                       );

            if (rest.Length > 1)
                return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

            var points = cli.GridOperator.Own.Entity.GridConnectionPoints;

            if (points.Count == 0)
                return Task.FromResult<String[]>([ $"The grid operator '{cli.GridOperator.Own.Id}' has no grid connection point yet: '{CommandName} add <id> <voltageLevel> <kW>' adds one." ]);

            var width = points.Max(point => point.Id.ToString().Length);

            return Task.FromResult<String[]>([
                       .. points.Select(point => {
                              var json    = point.ToJSON();
                              var behind  = cli.GridOperator.LocalControllersAt(point.Id.ToString());
                              return $"{point.Id.ToString().PadRight(width)}  " +
                                     $"{json.Value<String>("voltageLevel") ?? "-",-16}  " +
                                     $"{(json["contractedImportPower"]?.Value<Double>() is Double watts ? $"{watts / 1000:0.###} kW" : "- kW"),12}  " +
                                     $"{behind.Count} local controller(s)" +
                                     (behind.Count > 0 ? $": {String.Join(", ", behind.Select(login => login.VENName))}" : "");
                          })
                   ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [<id> | add <id> <voltageLevel> <importPower kW> [<name>] | remove <id>] - the grid connection points of the grid operator";

        #endregion


        #region (private) Added(Arguments)

        private String[] Added(String[] Arguments)
        {

            if (!cli.MayDo(Permission.Edit(GridOperatorAccess.POI), $"{CommandName} add", out var refused))
                return [ refused ];

            if (Arguments.Length < 3 || !Double.TryParse(Arguments[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var kW))
                return [ $"Usage: {CommandName} add <id> <voltageLevel> <importPower kW> [<name>]; the voltage levels are {String.Join(", ", GridOperator.VoltageLevels)}." ];

            var json = new JObject(
                           new JProperty("@id",                    Arguments[0]),
                           new JProperty("voltageLevel",           Arguments[1]),
                           new JProperty("contractedImportPower",  kW * 1000)
                       );

            if (Arguments.Length > 3)
                json["name"] = new JObject(new JProperty("en", String.Join(" ", Arguments[3..])));

            return cli.GridOperator.TryAddGridConnectionPoint(json, cli.Who, out var point, out var error, out _)
                       ? [ $"The grid connection point '{point.Id}' was added to the grid operator '{cli.GridOperator.Own.Id}'." ]
                       : [ error ];

        }

        #endregion

        #region (private) Removed(Arguments)

        private String[] Removed(String[] Arguments)
        {

            if (!cli.MayDo(Permission.Edit(GridOperatorAccess.POI), $"{CommandName} remove", out var refused))
                return [ refused ];

            if (Arguments.Length != 1)
                return [ $"Usage: {CommandName} remove <id>" ];

            return cli.GridOperator.TryRemoveGridConnectionPoint(Arguments[0], cli.Who, out var error, out _)
                       ? [ $"The grid connection point '{Arguments[0]}' was removed." ]
                       : [ error ];

        }

        #endregion

    }

}
