namespace Fd2.Data;

/// <summary>
/// 오프닝 애니메이션 ANI.DAT — 「AFM - Animation File Manager」 아홉 편. 편마다 173바이트 머리([165] u16 컷 수),
/// 이어서 컷마다 u16 크기 · u16 명령 수 · 4바이트 예비 · 명령들. 320x200 한 장과 VGA 팔레트를 명령으로 고쳐 나간다.
/// </summary>
/// <remarks>
/// 명령: 0 팔레트를 한 값으로, 1 팔레트 768, 2 팔레트 RLE, 3 팔레트 일부(u8 무리 수, 무리마다 u8 시작 색 · u8 색 수 · 3×색 수 바이트),
/// 4 화면을 한 값으로, 5 화면 64000, 6 화면 RLE,
/// 7 점들(u16 개수, [u16 자리, 색]…), 8 줄 칠하기([u16 자리, 길이, 색]…), 9 줄 복사([u16 자리, 길이, 바이트들]…).
/// RLE 는 <c>11nnnnnn</c> 이면 다음 바이트를 n 번, 아니면 그 바이트 하나(fd2_hermes 의 해독을 옮긴 것).
/// </remarks>
public sealed class AfmPlayer
{
    public const int Width = 320, Height = 200;

    private readonly byte[] _data;
    private readonly List<int> _films = new();
    private readonly byte[] _pal = new byte[768];
    private readonly byte[] _px = new byte[Width * Height];
    private int _film = -1, _cut, _cuts, _pos;

    public AfmPlayer(string dir)
    {
        _data = File.ReadAllBytes(Path.Combine(dir, "ANI.DAT"));
        int n = (Dat.U32(_data, 6) - 6) / 4 - 1;   // 마지막 칸은 파일 끝 표시
        for (int i = 0; i < n; i++)
        {
            int o = Dat.U32(_data, 6 + 4 * i);
            if (o + 173 < _data.Length) _films.Add(o);
        }
    }

    public int Films => _films.Count;

    /// <summary>다음 컷을 BGRA 로 — 다 끝났으면 null.</summary>
    public uint[]? Next()
    {
        while (_cut >= _cuts)
        {
            if (++_film >= _films.Count) return null;
            int o = _films[_film];
            _cuts = Dat.U16(_data, o + 165);
            _cut = 0;
            _pos = o + 173;
            Array.Clear(_pal);
            Array.Clear(_px);
        }
        int size = Dat.U16(_data, _pos), commands = Dat.U16(_data, _pos + 2);
        Run(_pos + 8, Math.Min(_data.Length, _pos + 8 + size), commands);
        _pos += 8 + size;
        _cut++;

        var pal = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            uint Six(int k) { int v = _pal[3 * i + k] & 63; return (uint)((v << 2) | (v >> 4)); }
            pal[i] = 0xFF000000 | Six(0) << 16 | Six(1) << 8 | Six(2);
        }
        var bgra = new uint[_px.Length];
        for (int i = 0; i < bgra.Length; i++) bgra[i] = pal[_px[i]];
        return bgra;
    }

    private void Run(int p, int end, int commands)
    {
        for (int c = 0; c < commands && p < end; c++)
        {
            switch (_data[p++])
            {
                case 0x00:
                    Array.Fill(_pal, _data[p++]);
                    break;
                case 0x01:
                    _data.AsSpan(p, 768).CopyTo(_pal);
                    p += 768;
                    break;
                case 0x02:
                    p = Unrle(p, end, _pal);
                    break;
                case 0x03:
                {
                    // 자료에서는 안 쓰지만 원본 명령 표(0x5276A)에 있다
                    int groups = _data[p++];
                    for (int g = 0; g < groups && p + 2 <= end; g++)
                    {
                        int first = _data[p], count = _data[p + 1];
                        p += 2;
                        int k = Math.Max(0, Math.Min(Math.Min(3 * count, _pal.Length - 3 * first), end - p));
                        _data.AsSpan(p, k).CopyTo(_pal.AsSpan(3 * first));
                        p += 3 * count;
                    }
                    break;
                }
                case 0x04:
                    Array.Fill(_px, _data[p++]);
                    break;
                case 0x05:
                    _data.AsSpan(p, _px.Length).CopyTo(_px);
                    p += _px.Length;
                    break;
                case 0x06:
                    p = Unrle(p, end, _px);
                    break;
                case 0x07:
                {
                    int n = Dat.U16(_data, p); p += 2;
                    for (int i = 0; i < n && p + 3 <= end; i++, p += 3)
                    {
                        int at = Dat.U16(_data, p);
                        if (at < _px.Length) _px[at] = _data[p + 2];
                    }
                    break;
                }
                case 0x08:
                {
                    int n = Dat.U16(_data, p); p += 2;
                    for (int i = 0; i < n && p + 4 <= end; i++, p += 4)
                    {
                        int at = Dat.U16(_data, p), len = _data[p + 2];
                        _px.AsSpan(at, Math.Max(0, Math.Min(len, _px.Length - at))).Fill(_data[p + 3]);
                    }
                    break;
                }
                case 0x09:
                {
                    int n = Dat.U16(_data, p); p += 2;
                    for (int i = 0; i < n && p + 3 <= end; i++)
                    {
                        int at = Dat.U16(_data, p), len = _data[p + 2];
                        p += 3;
                        int k = Math.Max(0, Math.Min(Math.Min(len, _px.Length - at), end - p));
                        _data.AsSpan(p, k).CopyTo(_px.AsSpan(at));
                        p += len;
                    }
                    break;
                }
                default:
                    return;   // 모르는 명령 — 이 컷은 여기까지
            }
        }
    }

    private int Unrle(int p, int end, byte[] dst)
    {
        int d = 0;
        while (d < dst.Length && p < end)
        {
            byte b = _data[p++];
            if ((b & 0xC0) == 0xC0)
            {
                int n = b & 0x3F;
                byte v = p < end ? _data[p++] : (byte)0;
                dst.AsSpan(d, Math.Min(n, dst.Length - d)).Fill(v);
                d += n;
            }
            else dst[d++] = b;
        }
        return p;
    }
}
