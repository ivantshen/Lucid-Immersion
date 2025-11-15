using System;
using System.Text;
using Afference.HWInt;
using UnityEngine;

public sealed class AfferenceRingAndroidTransport : NativeTransport, IDisposable
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private readonly AfferenceAndroidBridge _bridge = new();
    private readonly int _openTimeoutMs;
#endif

    public AfferenceRingAndroidTransport(int openTimeoutMs = 0)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        _openTimeoutMs = Mathf.Max(0, openTimeoutMs);
#endif
    }

    public nint Open(string path, byte[] err_msg, uint max_len)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        ClearErr(err_msg);
        try
        {
            Debug.Log($"[Transport] Open '{path}' (timeoutMs={_openTimeoutMs})");
            if (!_bridge.OpenBlocking(path, _openTimeoutMs))
            {
                WriteErr(err_msg, max_len, $"Open failed or timed out for '{path}'.");
                return IntPtr.Zero;
            }
            Debug.Log("[Transport] Open OK");
            return (IntPtr)1; // synthetic non-zero handle
        }
        catch (Exception e)
        {
            WriteErr(err_msg, max_len, $"Open exception: {e.GetType().Name}: {e.Message}");
            return IntPtr.Zero;
        }
#else
        // unchanged...
        return IntPtr.Zero;
#endif
    }

    public void Close(nint hnd)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try { _bridge.CloseBlocking(); }
        catch (Exception e) { Debug.LogWarning($"[Transport] Close exception: {e}"); }
        Debug.Log("[Transport] Close done");
#endif
    }

    public int Status(nint hnd, byte[] err_msg, uint max_len)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return _bridge.IsOpen ? 0 : -1;
#else
        return -1;
#endif
    }

    public int Tx(nint hnd, byte[] buf, uint len)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            int n = checked((int)len);
            if (n <= 0) return 0;

            byte[] toSend = buf;
            if (buf.Length != n)
            {
                toSend = new byte[n];
                Buffer.BlockCopy(buf, 0, toSend, 0, n);
            }

            Debug.Log($"[Transport] Tx len={n}");
            int rc = _bridge.TxBlocking(toSend);
            return (rc == 0) ? n : -2;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Transport] Tx exception: {e}");
            return -3;
        }
#else
        return -1;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private byte[] _stash = Array.Empty<byte>();
    private int _stashLen = 0;

    private void StashAppend(byte[] src, int offset, int count)
    {
        if (count <= 0) return;
        int newLen = _stashLen + count;
        if (_stash.Length < newLen) Array.Resize(ref _stash, Math.Max(newLen, 2048));
        Buffer.BlockCopy(src, offset, _stash, _stashLen, count);
        _stashLen = newLen;
    }

    private int StashPopInto(byte[] dst, int maxLen)
    {
        if (_stashLen == 0) return 0;

        int zero = Array.IndexOf(_stash, (byte)0, 0, _stashLen);
        int toCopy = (zero >= 0) ? Math.Min(zero + 1, maxLen) : Math.Min(_stashLen, maxLen);

        Buffer.BlockCopy(_stash, 0, dst, 0, toCopy);

        int remain = _stashLen - toCopy;
        if (remain > 0) Buffer.BlockCopy(_stash, toCopy, _stash, 0, remain);
        _stashLen = remain;

        return toCopy;
    }
#endif

    public int Rx(nint hnd, byte[] dst, uint max_len, uint timeout_ms)
    {
        Debug.Log($"[Transport] Rx ENTER timeoutMs={timeout_ms}, max_len={max_len}");
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            // Drain any leftover bytes first
            int n = StashPopInto(dst, (int)max_len);
            if (n > 0) return n;

            Debug.Log($"[Transport] Rx waiting timeoutMs={timeout_ms}");
            var data = _bridge.RxBlocking((int)timeout_ms); // waits for onRx

            if (data == null) return -2;    // fatal
            if (data.Length == 0) return 0; // timeout / no data

            int zero = Array.IndexOf(data, (byte)0);
            if (zero >= 0)
            {
                int firstLen = zero + 1;
                int copyLen  = Math.Min(firstLen, (int)max_len);
                Buffer.BlockCopy(data, 0, dst, 0, copyLen);

                int tail = data.Length - firstLen;
                if (tail > 0) StashAppend(data, firstLen, tail);
                return copyLen;
            }

            int plain = Math.Min((int)max_len, data.Length);
            Buffer.BlockCopy(data, 0, dst, 0, plain);
            if (data.Length > plain) StashAppend(data, plain, data.Length - plain);
            return plain;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Transport] Rx exception: {e}");
            return -2;
        }
#else
        return -1;
#endif
    }

    public void Dispose() => Close(IntPtr.Zero);

    private static void ClearErr(byte[] err) { if (err == null) return; Array.Clear(err, 0, err.Length); }
    private static void WriteErr(byte[] err, uint maxLen, string msg)
    {
        if (err == null || maxLen == 0) return;
        var bytes = Encoding.ASCII.GetBytes(msg ?? "");
        int n = Mathf.Min((int)maxLen - 1, bytes.Length);
        Array.Copy(bytes, 0, err, 0, n);
        err[n] = 0;
    }
}
