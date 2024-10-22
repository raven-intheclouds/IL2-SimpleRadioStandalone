﻿using System;
using FragLabs.Audio.Codecs.Opus;

namespace FragLabs.Audio.Codecs
{
    /// <summary>
    ///     Opus audio decoder.
    /// </summary>
    public class OpusDecoder : IDisposable
    {
        private IntPtr _decoder;

        private bool disposed;

        private OpusDecoder(IntPtr decoder, int outputSamplingRate, int outputChannels)
        {
            _decoder = decoder;
            OutputSamplingRate = outputSamplingRate;
            OutputChannels = outputChannels;
            MaxDataBytes = 2000;
        }

        /// <summary>
        ///     Gets the output sampling rate of the decoder.
        /// </summary>
        public int OutputSamplingRate { get; private set; }

        /// <summary>
        ///     Gets the number of channels of the decoder.
        /// </summary>
        public int OutputChannels { get; }

        /// <summary>
        ///     Gets or sets the size of memory allocated for decoding data.
        /// </summary>
        public int MaxDataBytes { get; set; }

        /// <summary>
        ///     Gets or sets whether forward error correction is enabled or not.
        /// </summary>
        public bool ForwardErrorCorrection { get; set; }

        public void Dispose()
        {
            if (disposed)
                return;

            GC.SuppressFinalize(this);

            if (_decoder != IntPtr.Zero)
            {
                API.opus_decoder_destroy(_decoder);
                _decoder = IntPtr.Zero;
            }

            disposed = true;
        }

        /// <summary>
        ///     Creates a new Opus decoder.
        /// </summary>
        /// <param name="outputSampleRate">Sample rate to decode at (Hz). This must be one of 8000, 12000, 16000, 24000, or 48000.</param>
        /// <param name="outputChannels">Number of channels to decode.</param>
        /// <returns>A new <c>OpusDecoder</c>.</returns>
        public static OpusDecoder Create(int outputSampleRate, int outputChannels)
        {
            if ((outputSampleRate != 8000) &&
                (outputSampleRate != 12000) &&
                (outputSampleRate != 16000) &&
                (outputSampleRate != 24000) &&
                (outputSampleRate != 48000))
                throw new ArgumentOutOfRangeException("inputSamplingRate");
            if ((outputChannels != 1) && (outputChannels != 2))
                throw new ArgumentOutOfRangeException("inputChannels");

            IntPtr error;
            var decoder = API.opus_decoder_create(outputSampleRate, outputChannels, out error);
            if ((Errors) error != Errors.OK)
            {
                throw new Exception("Exception occured while creating decoder");
            }
            return new OpusDecoder(decoder, outputSampleRate, outputChannels);
        }

        /// <summary>
        ///     Produces PCM samples from Opus encoded data.
        /// </summary>
        /// <param name="inputOpusData">Opus encoded data to decode, <c>null</c> for dropped packet.</param>
        /// <param name="dataLength">Length of data to decode or skipped data if <paramref name="inputOpusData" /> is <c>null</c>.</param>
        /// <param name="decodedLength">Set to the length of the decoded sample data.</param>
        /// <returns>PCM audio samples.</returns>
        public unsafe byte[] Decode(byte[] inputOpusData, int dataLength, out int decodedLength, bool reset=false)
        {
            if (disposed)
                throw new ObjectDisposedException("OpusDecoder");

            IntPtr decodedPtr;
            var decoded = new byte[MaxDataBytes];

            var length = 0;
            fixed (byte* bdec = decoded)
            {
                decodedPtr = new IntPtr(bdec);

                if (reset)
                {
                    //https://notabug.org/xiph/opus/raw/v0.9.10/include/opus_defines.h
                    var ret = API.opus_decoder_ctl(_decoder, 4028); //reset opus state - packets missing and it'll get confused
                    if (ret < 0)
                    {
                        throw new Exception("Error Resetting Oppus");
                    }
                }

                if (inputOpusData != null)
                {
                    var frameCount = FrameCount(MaxDataBytes);
                    length = API.opus_decode(_decoder, inputOpusData, dataLength, decodedPtr, frameCount,
                        ForwardErrorCorrection ? 1 : 0);
                }
                else
                    length = API.opus_decode(_decoder, null, 0, decodedPtr, FrameCount(dataLength), 0);
            }
            decodedLength = length * 2;
            if (length < 0)
                throw new Exception("Decoding failed - " + (Errors) length);

            return decoded;
        }

        /// <summary>
        ///     Determines the number of frames that can fit into a buffer of the given size.
        /// </summary>
        /// <param name="bufferSize"></param>
        /// <returns></returns>
        public int FrameCount(int bufferSize)
        {
            //  seems like bitrate should be required
            var bitrate = 16;
            var bytesPerSample = bitrate / 8 * OutputChannels;
            return bufferSize / bytesPerSample;
        }

        ~OpusDecoder()
        {
            Dispose();
        }
    }
}