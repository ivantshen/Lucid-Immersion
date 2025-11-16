// This is a helper class to convert an AudioClip to a .wav byte array.
// You do not need to attach this to any GameObject.

using System;
using System.IO;
using UnityEngine;

public static class WavUtility
{
    private const int HEADER_SIZE = 44;

    public static byte[] FromAudioClip(AudioClip clip)
    {
        using (var memoryStream = new MemoryStream())
        {
            // Write the WAV header
            memoryStream.Write(new byte[HEADER_SIZE], 0, HEADER_SIZE);

            // Get the audio data as floats
            float[] data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);

            // Convert float data to 16-bit PCM (byte array)
            byte[] pcmData = new byte[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                short s = (short)(data[i] * 32767.0f);
                BitConverter.GetBytes(s).CopyTo(pcmData, i * 2);
            }

            // Write the PCM data
            memoryStream.Write(pcmData, 0, pcmData.Length);

            // Now, go back and write the proper header
            memoryStream.Seek(0, SeekOrigin.Begin);
            WriteHeader(memoryStream, clip.channels, clip.frequency, (uint)pcmData.Length);

            return memoryStream.ToArray();
        }
    }

    private static void WriteHeader(Stream stream, int channels, int sampleRate, uint dataSize)
    {
        uint fileSize = dataSize + HEADER_SIZE - 8;
        uint byteRate = (uint)(sampleRate * channels * 2); // 16-bit PCM
        ushort blockAlign = (ushort)(channels * 2);

        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
            writer.Write(fileSize);
            writer.Write(new char[4] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[4] { 'f', 'm', 't', ' ' });
            writer.Write(16); // PCM chunk size
            writer.Write((ushort)1); // Audio format (1 = PCM)
            writer.Write((ushort)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((ushort)16); // Bits per sample
            writer.Write(new char[4] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
        }
    }
}