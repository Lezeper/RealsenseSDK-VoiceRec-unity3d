using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Media;

public class VoiceSynthesis : MonoBehaviour {

	private PXCMSession session;
	private PXCMSpeechSynthesis vsynth;
	private PXCMAudio sample;
	private PXCMSpeechSynthesis.ProfileInfo pinfo;
	private pxcmStatus sts;
	private PXCMSession.ImplDesc desc;

	private MMD4M_LipSync lp;

	private MemoryStream fs;
	private BinaryWriter bw;

	public int curr_volume = 80;
	public Single curr_speech_rate = 100;
	public string sentence;
	public bool enableSyn;

	void Start () 
	{
		lp = gameObject.GetComponent<MMD4M_LipSync> ();

		enableSyn = false;
		Initialization ();
	}

	void Update () 
	{
		if(enableSyn)
		{
			enableSyn = false;
			Voice_Synthesis(sentence);
		}
	}

	void Initialization()
	{
		session = PXCMSession.CreateInstance();
		if (session==null)
			Debug.Log("Failed to create an SDK session");

		// initialize a module implementation
		desc = new PXCMSession.ImplDesc();
		//desc.friendlyName=module;
		desc.cuids[0] = PXCMSpeechSynthesis.CUID;

		//  creates an instance of the I/O module or the algorithm module
		sts = session.CreateImpl<PXCMSpeechSynthesis>(desc, out vsynth);
		if (sts< pxcmStatus.PXCM_STATUS_NO_ERROR) 
		{
			session.Dispose();
			Debug.Log("Failed to create the synthesis module");
		}

		vsynth.QueryProfile(2,out pinfo);
		pinfo.volume = curr_volume;
		pinfo.rate = curr_speech_rate;
		//pinfo.pitch = curr_pitch;
		sts = vsynth.SetProfile(pinfo);
		if (sts<pxcmStatus.PXCM_STATUS_NO_ERROR) 
		{
			vsynth.Dispose();
			session.Dispose();
			Debug.Log("Failed to initialize the synthesis module");
		}
	}

	void Voice_Synthesis(string sentence)
	{
		sts = vsynth.BuildSentence(1, sentence);
		if (sts >= pxcmStatus.PXCM_STATUS_NO_ERROR)
		{
			VoiceOut(pinfo.outputs);
			for (int i=0;;i++)
			{
				sample = vsynth.QueryBuffer(1, i);
				if (sample == null) break;
				//Debug.Log("Now Speaking!");

				RenderAudio(sample);
			}
			Close();
		}
	}

	void VoiceOut(PXCMAudio.AudioInfo ainfo)
	{
		fs = new MemoryStream();
		bw = new BinaryWriter(fs);
		
		bw.Write((int)0x46464952);  // chunkIdRiff:'FFIR'
		bw.Write((int)0);           // chunkDataSizeRiff
		bw.Write((int)0x45564157);  // riffType:'EVAW'
		bw.Write((int)0x20746d66);  // chunkIdFmt:' tmf'
		bw.Write((int)0x12);        // chunkDataSizeFmt
		bw.Write((short)1);         // compressionCode
		bw.Write((short)ainfo.nchannels);  // numberOfChannels
		bw.Write((int)ainfo.sampleRate);   // sampleRate
		bw.Write((int)(ainfo.sampleRate * 2 * ainfo.nchannels));        // averageBytesPerSecond
		bw.Write((short)(ainfo.nchannels * 2));   // blockAlign
		bw.Write((short)16);        // significantBitsPerSample
		bw.Write((short)0);         // extraFormatSize
		bw.Write((int)0x61746164);  // chunkIdData:'atad'
		bw.Write((int)0);           // chunkIdSizeData
	}

	bool RenderAudio(PXCMAudio audio)
	{
		PXCMAudio.AudioData adata;
		pxcmStatus sts = audio.AcquireAccess(PXCMAudio.Access.ACCESS_READ, PXCMAudio.AudioFormat.AUDIO_FORMAT_PCM, out adata);
		if (sts < pxcmStatus.PXCM_STATUS_NO_ERROR) return false;

		bw.Write(adata.ToByteArray());
		audio.ReleaseAccess(adata);
		return true;
	}

	void Close()
	{
		long pos = bw.Seek(0, SeekOrigin.Current);
		bw.Seek(0x2a, SeekOrigin.Begin); // chunkDataSizeData
		bw.Write((int)(pos - 46));
		bw.Seek(0x04, SeekOrigin.Begin); // chunkDataSizeRiff
		bw.Write((int)(pos - 8));
		bw.Seek(0, SeekOrigin.Begin);

		WAV wav = new WAV(fs.ToArray());
		//Debug.Log(wav);
		AudioClip audioClip = AudioClip.Create("testSound", wav.SampleCount, 1,wav.Frequency, false, false);
		audioClip.SetData(wav.LeftChannel, 0);
		lp.audioClip = audioClip;
		lp.Play ();

		//SoundPlayer sp = new SoundPlayer(fs);
		//sp.PlaySync();
		//sp.Dispose();
		
		bw.Close();
		fs.Close();
	}
}

// WAV byte[] to AudioClip
public class WAV  {
	
	// convert two bytes to one float in the range -1 to 1
	static float bytesToFloat(byte firstByte, byte secondByte) {
		// convert two bytes to one short (little endian)
		short s = (short)((secondByte << 8) | firstByte);
		// convert to range from -1 to (just below) 1
		return s / 32768.0F;
	}
	
	static int bytesToInt(byte[] bytes,int offset=0){
		int value=0;
		for(int i=0;i<4;i++){
			value |= ((int)bytes[offset+i])<<(i*8);
		}
		return value;
	}
	
	private static byte[] GetBytes(string filename){
		return File.ReadAllBytes(filename);
	}
	// properties
	public float[] LeftChannel{get; internal set;}
	public float[] RightChannel{get; internal set;}
	public int ChannelCount {get;internal set;}
	public int SampleCount {get;internal set;}
	public int Frequency {get;internal set;}
	
	// Returns left and right double arrays. 'right' will be null if sound is mono.
	public WAV(string filename):
	this(GetBytes(filename)) {}
	
	public WAV(byte[] wav){
		
		// Determine if mono or stereo
		ChannelCount = wav[22];     // Forget byte 23 as 99.999% of WAVs are 1 or 2 channels
		
		// Get the frequency
		Frequency = bytesToInt(wav,24);
		
		// Get past all the other sub chunks to get to the data subchunk:
		int pos = 12;   // First Subchunk ID from 12 to 16
		
		// Keep iterating until we find the data chunk (i.e. 64 61 74 61 ...... (i.e. 100 97 116 97 in decimal))
		while(!(wav[pos]==100 && wav[pos+1]==97 && wav[pos+2]==116 && wav[pos+3]==97)) {
			pos += 4;
			int chunkSize = wav[pos] + wav[pos + 1] * 256 + wav[pos + 2] * 65536 + wav[pos + 3] * 16777216;
			pos += 4 + chunkSize;
		}
		pos += 8;
		
		// Pos is now positioned to start of actual sound data.
		SampleCount = (wav.Length - pos)/2;     // 2 bytes per sample (16 bit sound mono)
		if (ChannelCount == 2) SampleCount /= 2;        // 4 bytes per sample (16 bit stereo)
		
		// Allocate memory (right will be null if only mono sound)
		LeftChannel = new float[SampleCount];
		if (ChannelCount == 2) RightChannel = new float[SampleCount];
		else RightChannel = null;
		
		// Write to double array/s:
		int i=0;
		while (pos < wav.Length) {
			LeftChannel[i] = bytesToFloat(wav[pos], wav[pos + 1]);
			pos += 2;
			if (ChannelCount == 2) {
				RightChannel[i] = bytesToFloat(wav[pos], wav[pos + 1]);
				pos += 2;
			}
			i++;
		}
	}
	
	public override string ToString ()
	{
		return string.Format ("[WAV: LeftChannel={0}, RightChannel={1}, ChannelCount={2}, SampleCount={3}, Frequency={4}]", LeftChannel, RightChannel, ChannelCount, SampleCount, Frequency);
	}
}
	

