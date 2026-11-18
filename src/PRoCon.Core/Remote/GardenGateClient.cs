using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PRoCon.Core.Maps;
using PRoCon.Core.Players;

namespace PRoCon.Core.Remote
{
    /// <summary>
    /// Client for the GardenGate RCON protocol used by Plants vs. Zombies
    /// Garden Warfare 1/2 and Battle for Neighborville. The transport is
    /// Frostbite compatible, but several packets use a different field
    /// layout, so the affected dispatch methods are overridden here.
    /// </summary>
    public class GardenGateClient : BFClient
    {
        private readonly string _reportedGameType;

        public override string GameType
        {
            get
            {
                // GardenGate reports the real game in the `version` reply
                // (GW1 / GW2 / BFN). GW and GardenGate are aliases for GW1.
                switch (_reportedGameType?.ToUpperInvariant())
                {
                    case "GW2": return "GW2";
                    case "BFN":
                    case "GW3": return "BFN";
                    case "GW":
                    case "GW1":
                    case "GARDENGATE":
                    default: return "GW1";
                }
            }
        }

        public GardenGateClient(FrostbiteConnection connection, string reportedGameType = null)
            : base(connection)
        {
            _reportedGameType = reportedGameType;
            // The base class registers every command below against its virtual
            // dispatch methods, so overriding those methods is enough to
            // customise the parsing. Only the GardenGate-specific event
            // patterns need to be added to the packet filter.
            GetPacketsPattern = new Regex(GetPacketsPattern.ToString() +
                "|^player\\.onJoin$|^player\\.onLeave$|^player\\.onChat$|^player\\.onKicked$|^server\\.onLevelLoaded$",
                RegexOptions.Compiled);
        }

        public override void FetchStartupVariables()
        {
            // GardenGate only implements a small subset of the Frostbite
            // commands, so avoid requesting the vars.* settings it does not
            // support.
            SendServerinfoPacket();
            SendVersionPacket();
            SendHelpPacket();
            SendAdminListPlayersPacket(new CPlayerSubset(CPlayerSubset.PlayerSubsetType.All));
            SendBanListListPacket();
            SendMapListListRoundsPacket();
        }

        #region Overridden events

        public override event ServerInfoHandler ServerInfo;
        public override event ListPlayersHandler ListPlayers;
        public override event PlayerLeaveHandler PlayerLeft;
        public override event GlobalChatHandler GlobalChat;
        public override event TeamChatHandler TeamChat;
        public override event BanListAddHandler BanListAdd;
        public override event BanListRemoveHandler BanListRemove;
        public override event BanListListHandler BanListList;
        public override event MapListListedHandler MapListListed;
        public override event MapListAppendedHandler MapListMapAppended;
        public override event MapListMapInsertedHandler MapListMapInserted;

        #endregion

        #region Response handlers

        protected override void DispatchServerInfoResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            if (cpRecievedPacket.Words.Count < 7 || ServerInfo == null)
            {
                return;
            }

            // GardenGate: [ serverInfo, name, players, maxPlayers, map, gameMode, round, (totalRounds) ]
            var serverInfo = new CServerInfo
            {
                ServerName = cpRecievedPacket.Words[1],
                PlayerCount = ParseInt(cpRecievedPacket.Words[2]),
                MaxPlayerCount = ParseInt(cpRecievedPacket.Words[3]),
                Map = cpRecievedPacket.Words[4],
                GameMode = cpRecievedPacket.Words[5],
                CurrentRound = ParseInt(cpRecievedPacket.Words[6]),
                TotalRounds = cpRecievedPacket.Words.Count > 7 ? ParseInt(cpRecievedPacket.Words[7]) : 0
            };

            ServerInfo(this, serverInfo);
        }

        protected override void DispatchAdminListPlayersResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            if (ListPlayers == null)
            {
                return;
            }

            // GardenGate: [ name, teamId, squadId, kills, deaths, score, ping ] per player.
            var players = new List<CPlayerInfo>();
            for (int i = 1; i + 6 < cpRecievedPacket.Words.Count; i += 7)
            {
                players.Add(new CPlayerInfo
                {
                    SoldierName = cpRecievedPacket.Words[i],
                    TeamID = ParseInt(cpRecievedPacket.Words[i + 1]),
                    SquadID = ParseInt(cpRecievedPacket.Words[i + 2]),
                    Kills = ParseInt(cpRecievedPacket.Words[i + 3]),
                    Deaths = ParseInt(cpRecievedPacket.Words[i + 4]),
                    Score = ParseInt(cpRecievedPacket.Words[i + 5]),
                    Ping = ParseInt(cpRecievedPacket.Words[i + 6])
                });
            }

            CPlayerSubset subset = cpRequestPacket.Words.Count >= 2
                ? new CPlayerSubset(cpRequestPacket.Words.GetRange(1, cpRequestPacket.Words.Count - 1))
                : new CPlayerSubset(CPlayerSubset.PlayerSubsetType.All);

            ListPlayers(this, players, subset);
        }

        protected override void DispatchBanListAddResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            if (BanListAdd == null || cpRequestPacket.Words.Count < 3)
            {
                return;
            }

            // Frostbite: banList.add <idType> <id> <banType> [length] [reason]
            string idType = cpRequestPacket.Words[1];
            string id = cpRequestPacket.Words[2];
            CBanInfo info;
            if (idType == "name" || idType == "persona" || idType == "ip" || idType == "guid")
            {
                info = new CBanInfo(idType, id,
                    new TimeoutSubset(TimeoutSubset.TimeoutSubsetType.Permanent), String.Empty);
            }
            else
            {
                // Legacy GardenGate: banList.add <name> <reason> [machineId]
                string machineId = cpRequestPacket.Words.Count >= 4 ? cpRequestPacket.Words[3] : String.Empty;
                info = new CBanInfo(cpRequestPacket.Words[1], String.Empty, machineId,
                    new TimeoutSubset(TimeoutSubset.TimeoutSubsetType.None), cpRequestPacket.Words[2]);
            }

            BanListAdd(this, info);
        }

        protected override void DispatchBanListRemoveResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            if (BanListRemove == null)
            {
                return;
            }

            // Frostbite: banList.remove <idType> <id>; legacy: <name>
            string id = cpRequestPacket.Words.Count >= 3
                ? cpRequestPacket.Words[2]
                : cpRequestPacket.Words.Count >= 2 ? cpRequestPacket.Words[1] : String.Empty;
            if (id.Length > 0)
            {
                BanListRemove(this, new CBanInfo("name", id));
            }
        }

        protected override void DispatchBanListListResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            if (BanListList == null)
            {
                return;
            }

            // PRoCon pages through the ban list in blocks of 100 and stops when
            // it receives an empty page, so echo back the requested offset.
            int startOffset = 0;
            if (cpRequestPacket.Words.Count >= 2)
                int.TryParse(cpRequestPacket.Words[1], out startOffset);

            // GardenGate: [ index, identity, machineId, reason ] per ban.
            // GardenGate stores name bans in `names` and ip/guid bans in
            // `machineId`, so infer the id type from which field is populated.
            var bans = new List<CBanInfo>();
            for (int i = 1; i + 3 < cpRecievedPacket.Words.Count; i += 4)
            {
                int offset;
                if (!int.TryParse(cpRecievedPacket.Words[i], out offset))
                {
                    offset = bans.Count;
                }

                string identity = cpRecievedPacket.Words[i + 1];
                string machineId = cpRecievedPacket.Words[i + 2];
                string reason = cpRecievedPacket.Words[i + 3];

                CBanInfo info;
                if (String.IsNullOrEmpty(identity) && !String.IsNullOrEmpty(machineId))
                {
                    string idType = machineId.Contains(".") ? "ip" : "guid";
                    info = new CBanInfo(idType, machineId,
                        new TimeoutSubset(TimeoutSubset.TimeoutSubsetType.Permanent), reason);
                }
                else
                {
                    info = new CBanInfo("name", identity,
                        new TimeoutSubset(TimeoutSubset.TimeoutSubsetType.Permanent), reason);
                }
                info.Offset = offset;
                bans.Add(info);
            }

            BanListList(this, startOffset, bans);
        }

        protected override void DispatchMapListListResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            if (MapListListed == null)
            {
                return;
            }

            // GardenGate: [ index, mapFileName, gameMode, rounds, totalRounds ] per entry.
            var maps = new List<MaplistEntry>();
            for (int i = 1; i + 4 < cpRecievedPacket.Words.Count; i += 5)
            {
                int index;
                int rounds;
                int.TryParse(cpRecievedPacket.Words[i], out index);
                int.TryParse(cpRecievedPacket.Words[i + 3], out rounds);

                maps.Add(new MaplistEntry(cpRecievedPacket.Words[i + 2], cpRecievedPacket.Words[i + 1], rounds, index));
            }

            MapListListed(this, maps);
        }

        #endregion

        #region Request handlers

        protected override void DispatchPlayerOnLeaveRequest(FrostbiteConnection sender, Packet cpRequestPacket)
        {
            // GardenGate sends [ player.onLeave, name ] with no player details.
            if (cpRequestPacket.Words.Count >= 2 && PlayerLeft != null)
            {
                PlayerLeft(this, cpRequestPacket.Words[1], null);
            }
        }

        protected override void DispatchPlayerOnChatRequest(FrostbiteConnection sender, Packet cpRequestPacket)
        {
            if (cpRequestPacket.Words.Count < 3)
            {
                return;
            }

            string speaker = cpRequestPacket.Words[1];
            string message = cpRequestPacket.Words[2];

            // GardenGate appends the team id when the message was team chat.
            int teamId;
            if (cpRequestPacket.Words.Count >= 4 && int.TryParse(cpRequestPacket.Words[3], out teamId))
            {
                if (TeamChat != null)
                {
                    TeamChat(this, speaker, message, teamId);
                }
            }
            else if (GlobalChat != null)
            {
                GlobalChat(this, speaker, message);
            }
        }

        #endregion

        #region Send commands

        public virtual void SendBanListAddPacket(string name, string reason, string machineId = "")
        {
            if (IsLoggedIn)
            {
                if (String.IsNullOrEmpty(machineId))
                {
                    BuildSendPacket("banList.add", name, reason);
                }
                else
                {
                    BuildSendPacket("banList.add", name, reason, machineId);
                }
            }
        }

        public virtual void SendBanListRemovePacket(string name)
        {
            if (IsLoggedIn)
            {
                BuildSendPacket("banList.remove", name);
            }
        }

        public override void SendBanListListPacket()
        {
            if (IsLoggedIn)
            {
                BuildSendPacket("banList.list");
            }
        }

        public override void SendBanListSavePacket()
        {
            if (IsLoggedIn)
            {
                BuildSendPacket("banList.save");
            }
        }

        public virtual void SendMapListListPacket()
        {
            if (IsLoggedIn)
            {
                BuildSendPacket("mapList.list");
            }
        }

        // GardenGate accepts <fileName> <gameMode> <rounds> so the game mode
        // selected in the UI survives the round trip. The base client only
        // sends <fileName> <rounds>, which would show up as "default".
        public override void SendMapListAppendPacket(MaplistEntry map)
        {
            if (IsLoggedIn)
            {
                BuildSendPacket("mapList.append", map.MapFileName,
                    map.Gamemode ?? string.Empty,
                    (map.Rounds > 0 ? map.Rounds : 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        public override void SendMapListInsertPacket(MaplistEntry map)
        {
            if (IsLoggedIn)
            {
                BuildSendPacket("mapList.insert",
                    map.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    map.MapFileName,
                    map.Gamemode ?? string.Empty,
                    (map.Rounds > 0 ? map.Rounds : 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        protected override void DispatchMapListAppendResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            // Request: [ mapList.append, fileName, gameMode, rounds ]
            if (cpRequestPacket.Words.Count >= 4 && MapListMapAppended != null)
            {
                MaplistEntry mapEntry = new MaplistEntry(
                    cpRequestPacket.Words[2],
                    cpRequestPacket.Words[1],
                    ParseInt(cpRequestPacket.Words[3]));
                MapListMapAppended(this, mapEntry);
            }
            else
            {
                base.DispatchMapListAppendResponse(sender, cpRecievedPacket, cpRequestPacket);
            }
        }

        protected override void DispatchMapListInsertResponse(FrostbiteConnection sender, Packet cpRecievedPacket, Packet cpRequestPacket)
        {
            // Request: [ mapList.insert, index, fileName, gameMode, rounds ]
            if (cpRequestPacket.Words.Count >= 5 && MapListMapInserted != null)
            {
                MaplistEntry mapEntry = new MaplistEntry(
                    cpRequestPacket.Words[3],
                    cpRequestPacket.Words[2],
                    ParseInt(cpRequestPacket.Words[4]),
                    ParseInt(cpRequestPacket.Words[1]));
                MapListMapInserted(this, mapEntry);
            }
            else
            {
                base.DispatchMapListInsertResponse(sender, cpRecievedPacket, cpRequestPacket);
            }
        }

        #endregion

        private static int ParseInt(string value)
        {
            int result;
            int.TryParse(value, out result);
            return result;
        }
    }
}
