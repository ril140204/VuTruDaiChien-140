using System;
using System.IO;
using System.Text;

namespace QLTK.Client140
{
    /// <summary>
    /// Định dạng gói nhị phân QLTK (giai thích chính xác từ QLTK.Message2 runtime IL).
    ///
    ///   [1 byte Command (SByte)] [payload do BinaryWriter ghi]
    ///
    /// Payload: BinaryWriter ghi tuần tự các trường:
    ///   Int32  -> 4 byte little-endian
    ///   String -> BinaryWriter.Write(string): 7-bit encoded length (UTF-8) + bytes
    ///
    /// Chú ý: ctor KHÔNG ghi gì vào stream; Command duy nhất đặt qua
    /// thuộc tính Command, và GetData() chèn byte Command vào đầu.
    /// </summary>
    internal sealed class Message2
    {
        private readonly MemoryStream _ms;
        private readonly BinaryWriter _writer;

        public sbyte Command { get; set; }

        public Message2()
        {
            _ms = new MemoryStream();
            _writer = new BinaryWriter(_ms, Encoding.UTF8);
        }

        public void WriteInt(int value) => _writer.Write(value);
        public void WriteShort(short value) => _writer.Write(value);
        public void WriteByte(sbyte value) => _writer.Write(value);
        public void WriteString(string value) => _writer.Write(value ?? string.Empty);

        /// <summary>Trả về byte[] hoàn chỉnh: [Command][payload].</summary>
        public byte[] GetData()
        {
            _writer.Flush();
            byte[] payload = _ms.ToArray();
            byte[] result = new byte[payload.Length + 1];
            result[0] = unchecked((byte)Command);
            Buffer.BlockCopy(payload, 0, result, 1, payload.Length);
            return result;
        }

        public void Cleanup()
        {
            try { _writer?.Close(); } catch { }
            try { _ms?.Close(); } catch { }
        }
    }

    /// <summary>
    /// Các mã lệnh (Command) mà QLTK gửi xuống client.
    /// Trích từ runtime IL của QLTK.MainForm (vòng lặp newobj Message2).
    /// </summary>
    internal static class Cmd
    {
        public const sbyte SendFps = -102;              // sendFps(int fps)
        public const sbyte CongChiSo = -101;            // CongChiSo(int type)
        public const sbyte Xmap = -100;                 // Xmap(int mapId)
        public const sbyte ChangeZone = -99;            // changeZone(int zoneid)
        public const sbyte NhatZGem = -98;              // nhat_zGem(int type)
        public const sbyte SendTanCong = -97;           // sendTanCong(int typeId)
        public const sbyte KepFullKhu = -96;            // kepfullkhu(int map, int zoneid)
        public const sbyte SendHp = -95;                // sendHp(string text)
        public const sbyte TurnOffWinRuyBang = -94;     // sendTurnOffWinRuyBang()
        public const sbyte Resize = -93;                // resize(int w, int h)
        public const sbyte dung5BinhItem = -92;         // dung5BinhItem()
        public const sbyte mua5BinhItem = -91;          // mua5BinhItem()
        public const sbyte mua100Dau2 = -90;            // mua100Dau2()
        public const sbyte UpdateInfo = -89;            // updateInfo()
        public const sbyte muaDoLv3x = -88;             // muaDoLv3x()
    }
}
