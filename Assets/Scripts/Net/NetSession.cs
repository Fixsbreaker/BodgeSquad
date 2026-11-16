using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BodgeSquad.Net
{
    // Starting and joining games.
    //   Online: the host gets a short room code; friends type it in from anywhere (no router setup).
    //   LAN:    the host listens on a port; friends on the same network join by IP.
    // Up to 4 bodgers per game. If the host leaves, everyone returns to the menu.
    public class NetSession : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        public const ushort LanPort = 7777;

        public static NetSession Instance { get; private set; }

        public string Status { get; private set; } = "";
        public string RoomCode { get; private set; }
        public bool Busy { get; private set; }
        public bool InGame => NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        ISession _session;
        NetworkManager Net => NetworkManager.Singleton;
        UnityTransport Transport => Net.GetComponent<UnityTransport>();

        void Awake() => Instance = this;

        void Start()
        {
            Net.ConnectionApprovalCallback = Approve;
            Net.OnClientDisconnectCallback += OnDisconnected;
            AutoStartFromCommandLine();
        }

        void OnDestroy()
        {
            if (Net != null) Net.OnClientDisconnectCallback -= OnDisconnected;
        }

        // Room for 4: the fifth is turned away politely.
        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
        {
            bool room = Net.ConnectedClientsIds.Count < MaxPlayers;
            res.Approved = room;
            res.CreatePlayerObject = room;
            res.Reason = room ? "" : "The house is full (4 bodgers max)";
        }

        void OnDisconnected(ulong clientId)
        {
            if (Net.IsServer) { Debug.Log($"[Net] Client {clientId} left"); return; }
            if (clientId != Net.LocalClientId) return;
            string why = string.IsNullOrEmpty(Net.DisconnectReason) ? "Disconnected from the host" : Net.DisconnectReason;
            Debug.Log($"[Net] {why}");
            _ = BackToMenu(why);
        }

        // ------------------------------------------------------------ online (room code)
        async Task<bool> EnsureServices()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn) return true;
            try
            {
                Status = "Connecting to Unity services…";
                // A fresh profile per launch, so two copies on one PC are two different players
                var options = new InitializationOptions().SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return true;
            }
            catch (Exception e)
            {
                Status = Application.cloudProjectId == "" || string.IsNullOrEmpty(Application.cloudProjectId)
                    ? "Online play needs the project linked to Unity Cloud (Edit > Project Settings > Services). LAN works meanwhile."
                    : $"Couldn't reach Unity services: {e.Message}";
                Debug.LogWarning($"[Net] Services: {e}");
                return false;
            }
        }

        public async void HostOnline()
        {
            if (Busy || InGame) return;
            Busy = true;
            try
            {
                if (!await EnsureServices()) return;
                Status = "Creating a room…";
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                RoomCode = _session.Code;
                Status = $"Room code: {RoomCode}";
                Debug.Log($"[Net] Hosting online, code {RoomCode}");
            }
            catch (Exception e) { Fail("Couldn't create a room", e); }
            finally { Busy = false; }
        }

        public async void JoinOnline(string code)
        {
            if (Busy || InGame) return;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4) { Status = "Type the room code first"; return; }
            Busy = true;
            try
            {
                if (!await EnsureServices()) return;
                Status = $"Joining {code}…";
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                RoomCode = _session.Code;
                Status = $"Room code: {RoomCode}";
                Debug.Log($"[Net] Joined online room {RoomCode}");
            }
            catch (Exception e) { Fail($"Couldn't join {code}", e); }
            finally { Busy = false; }
        }

        // ------------------------------------------------------------ LAN (IP)
        public void HostLan()
        {
            if (Busy || InGame) return;
            Transport.SetConnectionData("0.0.0.0", LanPort, "0.0.0.0");
            if (Net.StartHost())
            {
                RoomCode = null;
                Status = $"Hosting on LAN: friends join {LocalIp()}";
                Debug.Log($"[Net] Hosting LAN on port {LanPort} ({LocalIp()})");
            }
            else Status = "Couldn't start hosting (is the port in use?)";
        }

        public void JoinLan(string ip)
        {
            if (Busy || InGame) return;
            ip = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
            Transport.SetConnectionData(ip, LanPort);
            Status = Net.StartClient() ? $"Joining {ip}…" : $"Couldn't connect to {ip}";
            Debug.Log($"[Net] {Status}");
        }

        public static string LocalIp()
        {
            try
            {
                foreach (var a in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a))
                        return a.ToString();
            }
            catch { /* offline */ }
            return "127.0.0.1";
        }

        // ------------------------------------------------------------ leaving
        public async void Leave() => await BackToMenu("You left the game");

        async Task BackToMenu(string why)
        {
            try { if (_session != null) await _session.LeaveAsync(); } catch { /* already gone */ }
            _session = null;
            RoomCode = null;
            if (Net != null && (Net.IsClient || Net.IsServer)) Net.Shutdown();
            Status = why;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            PendingStatus = why;
            // Fresh copy of the house for the next game (the NetworkManager survives the reload)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public static string PendingStatus;

        void Fail(string what, Exception e)
        {
            Status = $"{what}: {e.Message}";
            Debug.LogWarning($"[Net] {what}: {e}");
        }

        // ------------------------------------------------------------ test hooks
        // -bodgeHostLan | -bodgeJoinLan <ip> | -bodgeQuitAfter <seconds>
        void AutoStartFromCommandLine()
        {
            if (!string.IsNullOrEmpty(PendingStatus)) { Status = PendingStatus; PendingStatus = null; }
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-bodgeHostLan") HostLan();
                if (args[i] == "-bodgeJoinLan" && i + 1 < args.Length) JoinLan(args[i + 1]);
                if (args[i] == "-bodgeQuitAfter" && i + 1 < args.Length && float.TryParse(args[i + 1], out var t))
                    Invoke(nameof(QuitNow), t);
            }
        }

        void QuitNow()
        {
            Debug.Log("[Net] Test run over, quitting");
            Application.Quit();
        }
    }
}
