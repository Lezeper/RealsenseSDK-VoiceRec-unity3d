using UnityEngine;
using System.Collections;
using System;

public class VoiceRec : MonoBehaviour {
	
	private PXCMAudioSource source;
	private PXCMSpeechRecognition sr;
	// Session interface maintains the SDK context
	private PXCMSession session;

	private VoiceSynthesis vs;
	
	PXCMAudioSource.DeviceInfo device = new PXCMAudioSource.DeviceInfo();

	void Start () 
	{
		// create an instance of session interface
		session = PXCMSession.CreateInstance();
		vs = gameObject.GetComponent<VoiceSynthesis> ();
		AudioSorceCheck ();
		DoIt (session);
	}

	void AudioSorceCheck()
	{
		/* Create the AudioSource instance */
		source = session.CreateAudioSource();
		
		if(source != null)
		{
			source.ScanDevices();
			
			for (int i = 0; ; i++)
			{
				PXCMAudioSource.DeviceInfo dinfo;
				if (source.QueryDeviceInfo(i, out dinfo) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;

				if(dinfo.name.Contains("Creative VF")){
					device = dinfo;
					break;
				}
			}
		}
	}

	void OnAlert(PXCMSpeechRecognition.AlertData data)
	{
		Debug.Log(data.label);
	}

	// calls back when got recognition result
	void OnRecognition(PXCMSpeechRecognition.RecognitionData data)
	{
		if (data.scores[0].label < 0)
		{
			Debug.Log("sentence= " + data.scores[0].sentence);
			vs.enableSyn = true;
			vs.sentence = data.scores[0].sentence;

			if (data.scores[0].tags.Length > 0)
				Debug.Log("tags1= " + data.scores[0].tags);
		}
		else
		{
			for (int i = 0; i < PXCMSpeechRecognition.NBEST_SIZE; i++)
			{
				// The label of the recognized command
				int label = data.scores[i].label;
				// The recognition confidence level, from 0 to 100
				int confidence = data.scores[i].confidence;
				if (label < 0 || confidence == 0) continue;
				Debug.Log(label + confidence);
			}
			if (data.scores[0].tags.Length > 0)
				Debug.Log("tags2= " + data.scores[0].tags);
		}
	}

	public void DoIt(PXCMSession session) 
	{
		if (source == null) {
			Debug.Log("Stopped");
			return;
		} 

		// Set audio volume to 0.2 
		source.SetVolume(0.2f);
		
		// Set Audio Source 
		Debug.Log("Using device: " + device.name);
		source.SetDevice(device);
		
		// Set Module 
		PXCMSession.ImplDesc mdesc = new PXCMSession.ImplDesc();
		// The unique module identifier
		mdesc.iuid = 0;

		pxcmStatus sts = session.CreateImpl<PXCMSpeechRecognition>(out sr);

		if (sts >= pxcmStatus.PXCM_STATUS_NO_ERROR)
		{
			// Configure 
			PXCMSpeechRecognition.ProfileInfo pinfo;
			// chose language
			sr.QueryProfile(0, out pinfo);
			Debug.Log(pinfo.language);
			sr.SetProfile(pinfo);

			// Set Command/Control or Dictation 
			sr.SetDictation();

			// Initialization 
			Debug.Log("Init Started");
			PXCMSpeechRecognition.Handler handler = new PXCMSpeechRecognition.Handler();
			handler.onRecognition = OnRecognition;
			handler.onAlert=OnAlert;

			sts=sr.StartRec(source, handler);

			if (sts>=pxcmStatus.PXCM_STATUS_NO_ERROR) {
				Debug.Log("Init OK");
				/*
				// Wait until program exit 
				while (begin) {
					System.Threading.Thread.Sleep(5);
				}*/
			} else 
			{
				Debug.Log("Failed to initialize");
			}
		}else 
		{
			Debug.Log("Init Failed");
		}
	}

	void OnApplicationQuit() {
		sr.StopRec();
		Debug.Log("Stopped");
	}
}
