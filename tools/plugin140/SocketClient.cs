using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QLTK.Client140
{
    /// <summary>
    /// Káº¿t ná»‘i TCP tá»›i QLTK server (127.0.0.1:28082).
    ///
    /// GIAO THá»¨C (Ä‘Ă£ Ä‘á»‘i chiáº¿u runtime IL cá»§a QLTK.MainForm.HandleClientAsync
    /// vĂ  Client.SocketClient báº£n 132):
    ///
    ///   Client -&gt; Server : TEXT UTF-8, vĂ­ dá»¥ "STT:5", "lv: 12"
    ///       (server Ä‘á»c chunk rá»“i Encoding.UTF8.GetString(0..read),
    ///        KHĂ”NG tĂ¡ch dĂ²ng â€” nĂªn KHĂ”NG thĂªm '\n' cÅ©ng Ä‘Æ°á»£c,
    ///        nhÆ°ng thĂªm váº«n an toĂ n vĂ¬ server dĂ¹ng StartsWith)
    ///
    ///   Server -&gt; Client : BINARY, framing = 1 chunk = 1 packet
    ///       [1 byte Command (SByte Ă¢m)] [payload do BinaryWriter ghi]
    ///       Client Ä‘á»c: byte[0] = Command, pháº§n cĂ²n láº¡i = payload
    ///       (client ver132: len = buffer.Length, copy buffer[1..len-1]
    ///        vĂ o MemoryStream, BinaryReader Ä‘á»c theo command)
    ///
    ///   Payload: Int32 -&gt; 4 byte LE; String -&gt; 7-bit length prefix + UTF-8
    /// </summary>
    internal sealed class SocketClient : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _sendLock = new object();

        public event Action<string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        /// <summary>Sá»± kiá»‡n khi nháº­n Ä‘Æ°á»£c gĂ³i nhá»‹ phĂ¢n Ä‘Ă£ parse.</summary>
        public event Action<sbyte, byte[]> OnBinaryReceived;

        public bool IsConnected => _client != null && _client.Connected;

        public async Task ConnectAsync(string ip, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port).ConfigureAwait(false);
                _stream = _client.GetStream();

                OnConnected?.Invoke();
                _ = Task.Run(ReceiveLoop);
            }
            catch (Exception ex)
            {
                Log140.Error("Ket noi that bai: " + ex.Message);
            }
        }

        public void Disconnect()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
            OnDisconnected?.Invoke();
        }
        /// <summary>Gá»­i má»™t dĂ²ng text (UTF-8) tá»›i server.</summary>
        public void Send(string message)
        {
            if (_stream == null) return;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                lock (_sendLock)
                {
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Log140.Error("Send that bai: " + ex.Message);
            }
        }

        /// <summary>Gá»­i gĂ³i nhá»‹ phĂ¢n [cmd][payload] tá»›i server.</summary>
        public void SendBinary(sbyte command, params object[] payload)
        {
            if (_stream == null) return;
            try
            {
                var msg = new Message2 { Command = command };
                foreach (object p in payload)
                {
                    if (p is int i) msg.WriteInt(i);
                    else if (p is short sh) msg.WriteShort(sh);
                    else if (p is byte b) msg.WriteByte((sbyte)b);
                    else if (p is string s) msg.WriteString(s);
                }
                byte[] data = msg.GetData();
                msg.Cleanup();

                lock (_sendLock)
                {
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Log140.Error("SendBinary that bai: " + ex.Message);
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];

            try
            {
                while (IsConnected)
                {
                    int read = await _stream.ReadAsync(buffer, 0, buffer.Length)
                                     .ConfigureAwait(false);
                    if (read <= 0) break;

                    // Framing giá»‘ng client ver132: Má»–I CHUNK Ä‘á»c Ä‘Æ°á»£c = 1 packet
                    // [cmd][payload].  Server gá»­i tá»«ng lá»‡nh riĂªng láº» nĂªn 1 chunk
                    // = 1 lá»‡nh.  Tuy nhiĂªn TCP cĂ³ thá»ƒ gá»™p/nhiá»u, nĂªn ta dĂ¹ng
                    // bá»™ Ä‘á»‡m tĂ­ch lÅ©y vĂ  phĂ¢n tĂ­ch theo Ä‘á»™ dĂ i payload cá»§a command.
                    _recvBuffer.AddRange(new ArraySegment<byte>(buffer, 0, read));
                    ProcessBuffer();
                }
            }
            catch (Exception ex)
            {
                Log140.Warn("Ket noi bi dong: " + ex.Message);
            }
            finally
            {
                Disconnect();
            }
        }

        private readonly List<byte> _recvBuffer = new List<byte>(8192);

        private void ProcessBuffer()
        {
            while (true)
            {
                // Cáº§n tá»‘i thiá»ƒu 1 byte command
                if (_recvBuffer.Count < 1) return;

                byte cmdByte = _recvBuffer[0];
                sbyte cmd = unchecked((sbyte)cmdByte);

                // --- TEXT ---
                // Server chá»‰ gá»­i TEXT khi client gá»­i text; nhÆ°ng Ä‘á»ƒ cháº¯c cháº¯n,
                // ta coi byte >= 0x20 (printable) lĂ  báº¯t Ä‘áº§u text.
                if (cmdByte >= 0x20)
                {
                    int nl = _recvBuffer.IndexOf((byte)'\n');
                    if (nl < 0)
                    {
                        // chÆ°a cĂ³ newline -> chá» thĂªm (trá»« khi buffer quĂ¡ lá»›n)
                        if (_recvBuffer.Count > 64 * 1024) _recvBuffer.Clear();
                        return;
                    }

                    string line = Encoding.UTF8.GetString(_recvBuffer.GetRange(0, nl).ToArray()).Trim();
                    _recvBuffer.RemoveRange(0, nl + 1);
                    if (line.Length > 0)
                    {
                        MainThreadDispatcher.Enqueue(() =>
                        {
                            Log140.Info("Nhan text: " + line);
                            OnMessageReceived?.Invoke(line);
                        });
                    }
                    continue;
                }

                // --- BINARY ---
                if (!TryGetPacketLength(cmd, out int total))
                {
                    // Lá»‡nh cĂ³ payload Ä‘á»™ng (string) -> thá»­ Ä‘á»c length prefix
                    if (cmd == Cmd.SendHp)
                    {
                        if (!TryGetStringPacketLength(_recvBuffer, out int strTotal))
                        {
                            if (_recvBuffer.Count > 64 * 1024) _recvBuffer.Clear();
                            return;                     // chÆ°a Ä‘á»§ dá»¯ liá»‡u
                        }
                        total = strTotal;
                    }
                    else
                    {
                        // command khĂ´ng xĂ¡c Ä‘á»‹nh -> bá» 1 byte
                        _recvBuffer.RemoveAt(0);
                        continue;
                    }
                }

                if (_recvBuffer.Count < total) return;   // chÆ°a Ä‘á»§ payload

                byte[] packet = new byte[total];
                for (int i = 0; i < total; i++) packet[i] = _recvBuffer[i];
                _recvBuffer.RemoveRange(0, total);

                MainThreadDispatcher.Enqueue(() =>
                {
                    Log140.Info($"Nhan binary cmd={cmd} len={total}");
                    OnBinaryReceived?.Invoke(cmd, packet);
                    OnMessageReceived?.Invoke($"__BIN__{cmd}");
                });
            }
        }

        /// <summary>
        /// TĂ­nh tá»•ng Ä‘á»™ dĂ i gĂ³i chá»©a string:
        ///   [1 byte cmd][7-bit encoded length][UTF-8 bytes]
        /// </summary>
        private static bool TryGetStringPacketLength(List<byte> buf, out int total)
        {
            total = 0;

            // Ä‘á»c 7-bit encoded int (BinaryWriter.Write(string) dĂ¹ng Write7BitEncodedInt)
            int length = 0;
            int shift = 0;
            int idx = 1;   // bá» qua byte command
            while (true)
            {
                if (idx >= buf.Count) return false;         // chÆ°a Ä‘á»§ length prefix
                byte b = buf[idx++];
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 28) return false;                // length quĂ¡ lá»›n -> lá»—i
            }

            total = idx + length;                          // idx = 1(cmd) + lengthPrefixBytes
            return true;
        }

        /// <summary>
        /// TĂ­nh tá»•ng Ä‘á»™ dĂ i gĂ³i (1 byte cmd + payload) dá»±a trĂªn loáº¡i lá»‡nh.
        /// </summary>
        private static bool TryGetPacketLength(sbyte cmd, out int total)
        {
            int payload;

            switch (cmd)
            {
                case Cmd.SendFps:
                case Cmd.CongChiSo:
                case Cmd.Xmap:
                case Cmd.ChangeZone:
                case Cmd.NhatZGem:
                case Cmd.SendTanCong:
                    payload = 4; break;                       // int

                case Cmd.KepFullKhu:
                case Cmd.Resize:
                    payload = 8; break;                       // int + int

                case Cmd.SendHp:
                    // string: 7-bit length prefix (BinaryWriter) + UTF-8 bytes
                    // cáº§n Ä‘á»c length prefix trÆ°á»›c -> xá»­ lĂ½ riĂªng trong ProcessBuffer
                    total = 0;
                    return false;

                case Cmd.TurnOffWinRuyBang:
                case Cmd.dung5BinhItem:
                case Cmd.mua5BinhItem:
                case Cmd.mua100Dau2:
                case Cmd.UpdateInfo:
                case Cmd.muaDoLv3x:
                    payload = 0; break;

                default:
                    total = 0;
                    return false;
            }

            total = 1 + payload;
            return true;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
