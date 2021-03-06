﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SpeckleCore;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Serialization.Formatters.Binary;

namespace SpeckleUnity
{
	/// <summary>
	/// Manages the stream interactions for a single user in the scene and exposes parameters
	/// that control the output of the stream conversion.
	/// </summary>
	public class SpeckleUnityManager : MonoBehaviour, ISpeckleInitializer
	{
		/// <summary>
		/// Controls how the <c>RunStartBehaviourAsync ()</c> method executes.
		/// </summary>
		public StartMode onStartBehaviour = StartMode.LoginAndReceiveStreams;

		/// <summary>
		/// The server to send / receive streams from and authenticate against. Changing this value during
		/// runtime requires calling <c>InitializeAllClients ()</c> again.
		/// </summary>
		[Header ("Server Settings")]
		[SerializeField] protected string serverUrl = "https://hestia.speckle.works/api/";

		/// <summary>
		/// The email to login with on start if <c>onStartBehaviour</c> is set to at least <c>JustLogin</c>.
		/// </summary>
		public string startLoginEmail = "";

		/// <summary>
		/// The password to login with on start if <c>onStartBehaviour</c> is set to at least <c>JustLogin</c>.
		/// </summary>
		public string startLoginPassword = "";

		/// <summary>
		/// A cached reference to the current logged in user.
		/// </summary>
		[HideInInspector] public User loggedInUser = null;

		/// <summary>
		/// Assigns to the <c>MeshRenderer</c>s of every brep or mesh object for every stream handled by this manager.
		/// </summary>
		[Header ("Rendering Settings")]
		public Material meshMaterial;

		/// <summary>
		/// Assigns to the <c>LineRenderer</c>s of every line, curve or polyline object for every stream handled by this manager.
		/// </summary>
		public Material lineMaterial;

		/// <summary>
		/// Assigns to the <c>LineRenderer</c>s of every point object for every stream handled by this manager.
		/// </summary>
		public Material pointMaterial;

		/// <summary>
		/// An optional rendering rule to inject into the stream update process which defines how the stream looks in the scene.
		/// </summary>
		[SerializeField] protected internal RenderingRule renderingRule;

		/// <summary>
		/// 
		/// </summary>
		[Header ("Object Settings")]
		[SerializeField] protected bool recentreMeshTransforms = false;

		/// <summary>
		/// 
		/// </summary>
		[SerializeField] [Range (0.1f, 10f)] protected float lineWidth = 0.5f;

		/// <summary>
		/// 
		/// </summary>
		[SerializeField] [Range (0.1f, 10f)] protected float pointDiameter = 1;

		/// <summary>
		/// Speed value to allow for instantiation to happen gradually over many frames in case of 
		/// performance issues with large streams that get initialized / updated.
		/// </summary>
		[Header ("Receiver Settings")]
		public SpawnSpeed spawnSpeed = SpawnSpeed.TenPerFrame;

		/// <summary>
		/// A <c>UnityEvent</c> that is invoked each frame a stream update is progressed, including when it's initialised, for user code to 
		/// respond to that event. Passes some helpful data to inform that custom response including the percentage progress.
		/// </summary>
		[SerializeField] protected internal SpeckleUnityUpdateEvent onUpdateProgress = new SpeckleUnityUpdateEvent ();

		/// <summary>
		/// A list of all the <c>SpeckleUnityReceivers</c> this manager controls. Intended to only be directly editable via
		/// the inspector. During runtime you should make use of the <c>RemoveReceiver ()</c> or <c>AddReceiver ()</c> methods.
		/// </summary>
		[SerializeField] protected List<SpeckleUnityReceiver> receivers = new List<SpeckleUnityReceiver> ();

		/// <summary>
		/// How many receivers are currently on this manager. Includes both active and inactive receivers.
		/// </summary>
		public int ReceiverCount => receivers.Count;

		/// <summary>
		/// Initializes Speckle and assigns the scale factor of all geometry. Invokes the <c>RunStartBehaviour ()</c>
		/// coroutine.
		/// </summary>
		protected virtual async void Start ()
		{
			SpeckleInitializer.Initialize (false);

			SpeckleUnityMesh.RecentreMeshTransforms = recentreMeshTransforms;
			SpeckleUnityPolyline.LineWidth = lineWidth;
			SpeckleUnityPoint.PointDiameter = pointDiameter;

			await RunStartBehaviourAsync ();
		}

		/// <summary>
		/// Intended for running additional actions on start depending on the value of the <c>onStartBehaviour</c> enum.
		/// </summary>
		/// <returns>An async <c>Task</c> of the new operation.</returns>
		public virtual async Task RunStartBehaviourAsync ()
		{
			if (onStartBehaviour > StartMode.DoNothing)
			{
				SetServerUrl (serverUrl);
				await LoginAsync (startLoginEmail, startLoginPassword, null);
			}

			if (onStartBehaviour > StartMode.JustLogin)
			{
				if (!string.IsNullOrWhiteSpace (loggedInUser?.Apitoken))
				{
					await InitializeAllClientsAsync ();
				}
				else
				{
					Debug.LogError ("User has no API token.");
				}
			}
		}

		/// <summary>
		/// Logs out and assigns a new url to point to as the Speckle server.
		/// </summary>
		/// <param name="newServerUrl">The url of the new Speckle server to point to suffixed with "/api/".</param>
		public virtual void SetServerUrl (string newServerUrl)
		{
			if (string.IsNullOrWhiteSpace (newServerUrl))
			{
				throw new InvalidOperationException ("The server URL can't be null");
			}

			serverUrl = newServerUrl.Trim ();
		}

		/// <summary>
		/// Exposed method for users to call when trying to login to a Speckle server via code.
		/// </summary>
		/// <param name="email">The email of the account you wish to login with.</param>
		/// <param name="password">The corresponding password for the account.</param>
		/// <param name="callBack">A method callback which takes a <c>User</c>.</param>
		/// <returns>An async Task which can be awaited with a coroutine or just ignored.</returns>
		/// <remarks>If login was successful, the resulting user object is passed back. If failed, null
		/// is passed. Need to be using the <c>SpeckleCore</c> namespace to access this type.</remarks>
		public virtual async Task LoginAsync (string email, string password, Action<User> callBack)
		{
			if (string.IsNullOrWhiteSpace (email) || string.IsNullOrWhiteSpace (password))
			{
				throw new InvalidOperationException ("The email and password arguments both need to be provided if you wish to login.");
			}

			SpeckleApiClient loginClient = new SpeckleApiClient (serverUrl.Trim ());

			User user = new User { Email = email.Trim (), Password = password.Trim () };

			Debug.Log ("Atempting login");

			ResponseUser userGet = await loginClient.UserLoginAsync (user);

			if (userGet == null)
			{
				Debug.LogError ("Could not login");
				callBack?.Invoke (null);
			}
			else
			{
				loggedInUser = userGet.Resource;
				Debug.Log ("Logged in as " + loggedInUser.Name + " " + loggedInUser.Surname);

				loginClient?.Dispose (true);

				callBack?.Invoke (loggedInUser);
			}
		}

		/// <summary>
		/// Sets the <c>loggedInUser</c> to null and clears all the current receivers.
		/// </summary>
		public virtual void Logout ()
		{
			if (loggedInUser != null)
			{
				loggedInUser = null;
				ClearReceivers ();
			}
		}

		/// <summary>
		/// Exposed method for users to call when trying to download the meta data of the Projects the current
		/// logged in user is able to access.
		/// </summary>
		/// <param name="callBack">A method callback which takes a <c>Project</c> array.</param>
		/// <returns>An async Task which can be awaited with a coroutine or just ignored.</returns>
		/// <remarks>If download was successful, the resulting array is passed back. If failed, null
		/// is passed. Need to be using the <c>SpeckleCore</c> namespace to access this type.</remarks>
		public virtual async Task GetAllProjectMetaDataForUserAsync (Action<Project[]> callBack)
		{
			if (loggedInUser == null)
			{
				throw new UnauthorizedAccessException ("Need to be logged in before getting project data.");
			}

			SpeckleApiClient userProjectsClient = new SpeckleApiClient (serverUrl.Trim ());
			userProjectsClient.AuthToken = loggedInUser.Apitoken;

			ResponseProject projectsGet = await userProjectsClient.ProjectGetAllAsync ();

			if (projectsGet == null)
			{
				Debug.LogError ("Could not get projects for user");

				callBack?.Invoke (null);
			}
			else
			{
				List<Project> projects = projectsGet.Resources;

				Debug.Log ("Got " + projects.Count + " projects for user");

				callBack?.Invoke (projects.ToArray ());
			}
		}

		/// <summary>
		/// Exposed method for users to call when trying to download the meta data of the Streams the current
		/// logged in user is able to access. Use this to get the IDs of the streams you would later want to start
		/// receiving or use the rest of the data to populate your UI with data describing the Streams that are
		/// available.
		/// </summary>
		/// <param name="callBack">A method callback which takes a <c>SpeckleStream</c> array.</param>
		/// <returns>An async Task which can be awaited with a coroutine or just ignored.</returns>
		/// <remarks>If download was successful, the resulting array is passed back. If failed, null
		/// is passed. Need to be using the <c>SpeckleCore</c> namespace to access this type.</remarks>
		public virtual async Task GetAllStreamMetaDataForUserAsync (Action<SpeckleStream[]> callBack)
		{
			if (loggedInUser == null)
			{
				throw new UnauthorizedAccessException ("Need to be logged in before getting stream meta data.");
			}

			SpeckleApiClient userStreamsClient = new SpeckleApiClient (serverUrl.Trim ());
			userStreamsClient.AuthToken = loggedInUser.Apitoken;

			ResponseStream streamsGet = await userStreamsClient.StreamsGetAllAsync (null);

			if (streamsGet == null)
			{
				Debug.LogError ("Could not get streams for user");

				callBack?.Invoke (null);
			}
			else
			{
				List<SpeckleStream> streams = streamsGet.Resources;

				Debug.Log ("Got " + streams.Count + " streams for user");

				callBack?.Invoke (streams.ToArray ());
			}
		}

		/// <summary>
		/// Loops through each receiver and starts each of their initialization coroutines.
		/// </summary>
		/// <returns>An async Task which can be awaited with a coroutine or just ignored.</returns>
		public virtual async Task InitializeAllClientsAsync ()
		{
			Task[] tasks = new Task[receivers.Count];

			for (int i = 0; i < receivers.Count; i++)
			{
				tasks[i] = receivers[i].InitializeClient (this, serverUrl, loggedInUser.Apitoken);
			}

			try
			{
				await Task.WhenAll (tasks);
			}
			catch
			{
				throw;
			}
		}

		/// <summary>
		/// Since there is a weird bug in Unity with web socket responses not being able to invoke coroutines, we check a boolean 
		/// on each receiver on each frame to simulate that effect.
		/// </summary>
		protected virtual void Update ()
		{
			for (int i = 0; i < receivers.Count; i++)
			{
				receivers[i].OnWsMessageCheck ();
			}
		}

		/// <summary>
		/// Creates a new receiver to be managed by this manager instance. 
		/// </summary>
		/// <param name="streamID">The ID of the stream to receive. Authentication will be done using the <c>authToken</c>
		/// on the manager instance this method is being called on.</param>
		/// <param name="streamRoot">Optionally, you can provide a <c>Transform</c> for the geometry to be spawned
		/// under.</param>
		/// <param name="initialiseOnCreation">Optionally, the stream can have its <c>InitializeClient</c>
		/// method started after being created.</param>
		/// <param name="receiveUpdates"></param>
		/// <returns>An async Task which can be awaited with a coroutine or just ignored.</returns>
		public virtual async Task AddReceiverAsync (string streamID, Transform streamRoot = null, bool initialiseOnCreation = false, bool receiveUpdates = true)
		{
			SpeckleUnityReceiver newReceiver = new SpeckleUnityReceiver (streamID, streamRoot, receiveUpdates);
			receivers.Add (newReceiver);

			if (initialiseOnCreation)
				await newReceiver.InitializeClient (this, serverUrl, loggedInUser.Apitoken);
		}

		/// <summary>
		/// Remove the first receiver with a matching stream ID on this manager instance. Cleans up all GameObjects
		/// associated to that stream as well.
		/// </summary>
		/// <param name="streamID">The ID of the stream to be removed. If no matching ID is found, nothing will
		/// happen.</param>
		public virtual void RemoveReceiver (string streamID)
		{
			for (int i = 0; i < receivers.Count; i++)
			{
				if (receivers[i].streamID == streamID)
				{
					RemoveReceiver (i);
					return;
				}
			}

			RemoveReceiver (-1);
		}

		/// <summary>
		/// Remove the first receiver with a matching root <c>Transform</c> on this manager instance. Cleans
		/// up all GameObjects associated to that stream as well.
		/// </summary>
		/// <param name="streamRoot">The root object of the stream to be removed. If no matching root is 
		/// found, nothing will happen.</param>
		public virtual void RemoveReceiver (Transform streamRoot)
		{
			for (int i = 0; i < receivers.Count; i++)
			{
				if (receivers[i].streamRoot == streamRoot)
				{
					RemoveReceiver (i);
					return;
				}
			}

			RemoveReceiver (-1);
		}

		/// <summary>
		/// Remove a receiver of a given index in the list on this manager instance. Cleans up all GameObjects
		/// associated to that stream as well.
		/// </summary>
		/// <param name="receiverIndex">The index to to remove from.</param>
		public virtual void RemoveReceiver (int receiverIndex)
		{
			if (receiverIndex < 0 || receiverIndex >= receivers.Count)
				throw new ArgumentOutOfRangeException ("Receiver could not be removed because it does not exist");

			SpeckleUnityReceiver receiver = receivers[receiverIndex];
			receivers.RemoveAt (receiverIndex);

			receiver.RemoveContents ();
			receiver.client?.Dispose ();

			if (receiver.streamRoot.name.Contains ("Default Stream Root: "))
			{
				Destroy (receiver.streamRoot.gameObject);
			}
		}

		/// <summary>
		/// Calls <c>RemoveReceiver (int)</c> on all receivers on this manager instance.
		/// </summary>
		public virtual void ClearReceivers ()
		{
			for (int i = receivers.Count - 1; i >= 0; i--)
			{
				RemoveReceiver (i);
			}
		}

		/// <summary>
		/// Get a <c>SpeckleStream</c> object for each receiver.
		/// </summary>
		/// <returns>An array of <c>SpeckleStream</c>s.</returns>
		public virtual SpeckleStream[] GetCurrentReceivedStreamMetaData ()
		{
			return receivers.Select (r => r.client.Stream).ToArray ();
		}

		/// <summary>
		/// Checks through all receivers and looks up their <c>GameObject</c> to <c>SpeckleObject</c> dictionaries and outputs 
		/// the <c>SpeckleObject</c>. 
		/// </summary>
		/// <param name="gameObjectKey">The <c>GameObject</c> in the scene that represents the object to get data for.</param>
		/// <param name="speckleObjectData">The <c>SpeckleObject</c> that corresponds to the <c>GameObject</c>. Outputs as 
		/// null if the lookup failed.</param>
		/// <returns>Whether or not the lookup was successful.</returns>
		public virtual bool TryGetSpeckleObject (GameObject gameObjectKey, out SpeckleObject speckleObjectData)
		{
			speckleObjectData = null;

			if (gameObjectKey == null) return false;

			for (int i = 0; i < receivers.Count; i++)
			{
				bool gotValue = receivers[i].speckleObjectLookup.TryGetValue (gameObjectKey, out SpeckleObject speckleObject);

				if (gotValue)
				{
					speckleObjectData = speckleObject;
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="streamID"></param>
		public virtual List<float> GetNumbersFromStream (string streamID)
		{
			for (int i = 0; i < receivers.Count; i++)
			{
				if (receivers[i].streamID == streamID)
				{
					return GetNumbersFromStream (i);
				}
			}

			return GetNumbersFromStream (-1);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="receiverIndex"></param>
		/// <returns></returns>
		public virtual List<float> GetNumbersFromStream (int receiverIndex)
		{
			if (receiverIndex < 0 || receiverIndex >= receivers.Count)
				throw new ArgumentOutOfRangeException ("Receiver could not be accessed because it does not exist");

			return receivers[receiverIndex].numbers;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="streamID"></param>
		public virtual List<string> GetStringsFromStream (string streamID)
		{
			for (int i = 0; i < receivers.Count; i++)
			{
				if (receivers[i].streamID == streamID)
				{
					return GetStringsFromStream (i);
				}
			}

			return GetStringsFromStream (-1);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="receiverIndex"></param>
		/// <returns></returns>
		public virtual List<string> GetStringsFromStream (int receiverIndex)
		{
			if (receiverIndex < 0 || receiverIndex >= receivers.Count)
				throw new ArgumentOutOfRangeException ("Receiver could not be accessed because it does not exist");

			return receivers[receiverIndex].strings;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="streamID"></param>
		/// <returns></returns>
		public virtual List<Mesh> GetMeshesFromStream (string streamID)
		{
			for (int i = 0; i < receivers.Count; i++)
			{
				if (receivers[i].streamID == streamID)
				{
					return GetMeshesFromStream (i);
				}
			}

			return GetMeshesFromStream (-1);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="receiverIndex"></param>
		/// <returns></returns>
		public virtual List<Mesh> GetMeshesFromStream (int receiverIndex)
		{
			if (receiverIndex < 0 || receiverIndex >= receivers.Count)
				throw new ArgumentOutOfRangeException ("Receiver could not be accessed because it does not exist");

			return receivers[receiverIndex].meshes;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="streamID"></param>
		/// <returns></returns>
		public virtual List<MeshRenderer> GetMeshRenderersFromStream (string streamID)
		{
			for (int i = 0; i < receivers.Count; i++)
			{
				if (receivers[i].streamID == streamID)
				{
					return GetMeshRenderersFromStream (i);
				}
			}

			return GetMeshRenderersFromStream (-1);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="receiverIndex"></param>
		/// <returns></returns>
		public virtual List<MeshRenderer> GetMeshRenderersFromStream (int receiverIndex)
		{
			if (receiverIndex < 0 || receiverIndex >= receivers.Count)
				throw new ArgumentOutOfRangeException ("Receiver could not be accessed because it does not exist");

			return receivers[receiverIndex].meshRenderers;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="newRenderingRule"></param>
		public virtual void SetRenderingRule (RenderingRule newRenderingRule)
		{
			if (newRenderingRule == null)
				throw new ArgumentNullException ("Cannot set a null rendering rule.");

			renderingRule = newRenderingRule;

			for (int i = 0; i < receivers.Count; i++)
			{
				receivers[i].ReApplyRenderingRule ();
			}
		}

		/// <summary>
		/// Creates a bounding box that tightly encapsulates all objects in all current streams.
		/// </summary>
		/// <returns>A bounding box value encapsulating all stream objects in the scene.</returns>
		public virtual Bounds GetBoundsForAllReceivedStreams ()
		{
			if (receivers.Count == 0)
				return new Bounds (Vector3.zero, Vector3.one);

			MeshRenderer[] meshes = receivers[0].streamRoot.GetComponentsInChildren<MeshRenderer> ();

			Bounds bounds = meshes[0].bounds;

			for (int i = 1; i < meshes.Length; i++)
			{
				bounds.Encapsulate (meshes[i].bounds);
			}

			for (int i = 1; i < receivers.Count; i++)
			{
				MeshRenderer[] otherMeshes = receivers[i].streamRoot.GetComponentsInChildren<MeshRenderer> ();

				for (int j = 0; j < otherMeshes.Length; j++)
				{
					bounds.Encapsulate (otherMeshes[j].bounds);
				}
			}

			return bounds;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="newWidth"></param>
		public virtual void UpdateLineWidth (float newWidth)
		{
			if (newWidth <= 0)
			{
				throw new ArgumentException ("Line width needs to be greater than zero.");
			}

			if (newWidth == SpeckleUnityPolyline.LineWidth)
				return;

			SpeckleUnityPolyline.LineWidth = newWidth;

			for (int i = 0; i < receivers.Count; i++)
			{
				receivers[i].UpdateLineWidth ();
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="newWidth"></param>
		public virtual void UpdatePointDiameter (float newDiameter)
		{
			if (newDiameter <= 0)
			{
				throw new ArgumentException ("Point diameter needs to be greater than zero.");
			}

			if (newDiameter == SpeckleUnityPoint.PointDiameter)
				return;

			SpeckleUnityPoint.PointDiameter = newDiameter;

			for (int i = 0; i < receivers.Count; i++)
			{
				receivers[i].UpdatePointDiameter ();
			}
		}
	}

	/// <summary>
	/// An enum for controlling how quickly geometry objects get isntantiated into the scene.
	/// The Instant value simply means all objects will be spawned in the same frame. (Technically,
	/// it caps out at 2 billion, but who in the world is making streams bigger than 2 billion...)
	/// </summary>
	public enum SpawnSpeed : int
	{
		Instant = int.MaxValue,
		ThousandPerFrame = 1000,
		HundredPerFrame = 100,
		TenPerFrame = 10
	}

	/// <summary>
	/// An enum for controlling what the default start behaviour of a <c>SpeckleUnityManager</c>
	/// would be. 
	/// </summary>
	public enum StartMode : int
	{
		DoNothing = 0,
		JustLogin = 1,
		LoginAndReceiveStreams = 2
	}
}