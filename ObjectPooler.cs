using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Random = UnityEngine.Random;

/// <summary>
/// Singleton.
/// GameObjects that want to be pooled call CreatePool() with a TAG and configuration. 
/// All GameObjects are responsible for (de)activating pooled objects.
/// GameObjects never interface directly with their Pool, just the ObjectPooler
/// and pooled GameObjects.
/// Helpful to move this high in Script Execution Order!
/// </summary>
public class ObjectPooler : MonoBehaviour {

    public static ObjectPooler instance = null;

    private static int numObjectsPooled = 0;

    [Tooltip("This is used to set the transform.hierarchyCapacity of the ObjectPooler " +
    	"for a slight optimization due to pooling organization.")]
    [SerializeField] int approxNumOfPools = 10;

    private Dictionary<string, Pool> poolDictionary = new Dictionary<string, Pool>();

    private void Awake() {
        if (instance == null) instance = this;
        else Destroy(gameObject);

        transform.hierarchyCapacity = approxNumOfPools;

        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Gets the pool.
    /// Caller must perform null check!
    /// Logs a helpful error to client.
    /// </summary>
    /// <returns>The pool if there is a matching pool tag or else null.</returns>
    /// <param name="poolTag">Pool tag.</param>
    private Pool GetPool(string poolTag) {
        Pool pool = null;

        // get pool by reference with out!
        poolDictionary.TryGetValue(poolTag, out pool);

        if (pool == null)
            Debug.LogError(string.Format("Error getting a Pool with given Tag: {0}.\n" +
                "These are the current Pool Tags in the Object Pooler: {1}", poolTag, GetPoolTags()));

        return pool;
    }

    /// <summary>
    /// Creates the pool given a POOL_TAG and <paramref name="poolConfig"/>. The <paramref name="poolConfig"/> reference is 
    /// copied to another reference!
    /// This method avoids creating duplicate pools.
    /// Cache the associated POOL_TAG passed into the <paramref name="poolConfig"/> as a
    /// <see langword="static"/> string in the GameObject that needs pooling.
    /// </summary>
    /// <param name="POOL_TAG">Pool Tag to interface with Pooler</param>
    /// <param name="poolConfig">Pool config.</param>
    public void CreatePool(PoolConfig poolConfig) {
        string POOL_TAG = poolConfig.poolTag;
        Dictionary<string, Pool>.KeyCollection keyColl = poolDictionary.Keys;

        // early return to avoid dup errors
        if (keyColl.Contains(POOL_TAG)) return;
        
        // Organization in hierarchy
        Transform poolContainer = new GameObject(POOL_TAG).transform;
        poolContainer.SetParent(transform);

        // Create pool, add to pool dictionary
        Debug.Log("Creating pool with tag: " + POOL_TAG);
        Pool pool = new Pool(poolConfig, poolContainer);
        poolDictionary.Add(POOL_TAG, pool);
    }

    /// <summary>
    /// Gets the object from Pool given a poolTag.
    /// If a Pool can't be found, null is returned!
    /// </summary>
    /// <returns>
    /// The GO from pool if one is available or else null.
    /// The GO is active so do what you need with it ASAP!   
    /// </returns>
    /// <param name="poolTag">Pool tag.</param>
    public GameObject GetObjFromPool(string poolTag, bool getRandom = false) {
        Pool pool = GetPool(poolTag);
        if (pool == null) return null;

        return pool.GetObjFromPool(getRandom);
    }

    public GameObject GetObjFromPool(string poolTag, string prefabName) {
        Pool pool = GetPool(poolTag);
        if (pool == null) return null;

        return pool.GetObjFromPool(prefabName);
    }

    /// <summary>
    /// Helper to get all POOL_TAGs currently in Pooler
    /// </summary>
    /// <returns>The pool tags.</returns>
    public string[] GetPoolTags() {
        string[] poolTags = new string[poolDictionary.Count];
        Dictionary<string, Pool>.KeyCollection keyColl = poolDictionary.Keys;
        return keyColl.ToArray();
    }

    /// <summary>
    /// Resets the pools GameObjects to inactive
    /// </summary>
    /// <param name="poolTag">Pool tag.</param>
    public void ResetPool(string poolTag) {
        Pool pool = GetPool(poolTag);
        if (pool == null) return;
        pool.poolConfig.poolList.ForEach(go => go.SetActive(false));
    }

    /// <summary>
    /// Resets all pools in Pooler to inactive
    /// </summary>
    /// <param name="skipPoolTag">Pass Tags to skip resetting that pool</param>
    public void ResetAllPools(params string[] skipPoolTag) {
        Dictionary<string, Pool>.ValueCollection allPools = poolDictionary.Values;
        foreach(Pool pool in allPools) {
            if (skipPoolTag.Contains(pool.poolConfig.poolTag)) continue;
            ResetPool(pool.poolConfig.poolTag);
        }
    }

    /// <summary>
    /// Helper to move a GO to a target position
    /// Syncs the transforms of <paramref name="syncee"/> to <paramref name="syncer"/>
    /// </summary>
    /// <param name="syncee">Syncee.</param>
    /// <param name="syncer">Syncer.</param>
    public void SyncTransforms(Transform syncee, Transform syncer) {
        if (syncee == null || syncer == null) return;
        syncee.transform.position = syncer.transform.position;
        syncee.transform.rotation = syncer.transform.rotation;
    }

    public int GetNumPooledObjects() {
        return numObjectsPooled;
    }

    // ============================ INTERNAL CLASS ========================== //
    /// <summary>
    /// This is an internal private class as only the Object Pooler should know about it.
    /// The Pool holds GameObjects for efficiency. 
    /// </summary>
    private class Pool {
        
        public PoolConfig poolConfig;
        public Transform container;

        public Pool(PoolConfig config, Transform poolContainer) {
            poolConfig = config;
            container = poolContainer;

            // load up the pool
            for (int x = 0; x < poolConfig.poolAmount; x++) {
                GameObject randomPoolGO = poolConfig.poolPrefabs[Random.Range(0, poolConfig.poolPrefabs.Length)];
                GameObject poolGO = Instantiate(randomPoolGO, container);
                ObjectPooler.numObjectsPooled++;
                poolGO.SetActive(false);
                poolConfig.poolList.Add(poolGO);
            }
        }

        /// <summary>
        /// Finds the first inactive GO in the pool, marks it active and returns it.
        /// A random GO is returned if true is passed as <paramref name="getRandom"/>.
        /// If there are no inactive GOs to get, grow if auto grow is enabled,
        /// or else return null.
        /// It is less expensive to not <paramref name="getRandom"/>
        /// </summary>
        /// <returns>The first inactive GO in the Pool or else null.</returns>
        /// <param name="getRandom"><see langword="true"/> if client wants random inactive object from Pool</param>
        public GameObject GetObjFromPool(bool getRandom) {
            int inactiveIndex = -1; // assume everything is active

            // helpful if storing diff item drops in one pool
            if (getRandom) {
                bool hasInactive = false;
                int poolListCount = poolConfig.poolList.Count;
                for (int index = 0; index < poolListCount; index++) {
                    GameObject go = poolConfig.poolList[index];
                    if (go.activeInHierarchy) continue;

                    hasInactive = true;
                    // 0.87 just seems to be the magic # for getting a good randomness
                    if (Random.Range(0f, 0.99f) > 0.87f) {
                        inactiveIndex = index;
                        break;
                    }
                }
                // random calculations failed, just return first inactive
                if (inactiveIndex == -1 && hasInactive) inactiveIndex = poolConfig.poolList.FindIndex(go => !go.activeInHierarchy);
            }
            // Just find first inactive
            else inactiveIndex = poolConfig.poolList.FindIndex(go => !go.activeInHierarchy);

            // an inactive GO wasnt found. do auto-grow
            if (inactiveIndex == -1) {

                // create new active GO, add to list, and return to client
                GameObject randomPoolGO = poolConfig.poolPrefabs[Random.Range(0, poolConfig.poolPrefabs.Length)];
                GameObject newPoolGO = Instantiate(randomPoolGO, container);
                ObjectPooler.numObjectsPooled++;
                poolConfig.poolList.Add(newPoolGO);
                return newPoolGO;
            }

            GameObject pooledGO = poolConfig.poolList[inactiveIndex];
            pooledGO.SetActive(true);

            return pooledGO;
        }

        public GameObject GetObjFromPool(string prefabName) {
            int inactiveIndex = -1; // assume everything is active

            inactiveIndex = poolConfig.poolList.FindIndex(go => !go.activeInHierarchy && go.name.Equals(prefabName));

            // an inactive GO wasnt found. do auto-grow
            if (inactiveIndex == -1) {
                GameObject prefab = null;
                for (int x=0; x<poolConfig.poolPrefabs.Length; x++) {
                    GameObject currPrefab = poolConfig.poolPrefabs[x];
                    if (currPrefab.name.Equals(prefabName)) prefab = currPrefab;
                }

                // prefab name doesnt exist in prefab list
                if (prefab == null) {
                    Debug.LogErrorFormat("Prefab Name {0} is not within Pool {1}", prefabName, poolConfig.poolTag);
                    return null;
                }
                GameObject newPoolGO = Instantiate(prefab, container);
                ObjectPooler.numObjectsPooled++;
                poolConfig.poolList.Add(newPoolGO);
                return newPoolGO;
            }

            GameObject pooledGO = poolConfig.poolList[inactiveIndex];
            pooledGO.SetActive(true);

            return pooledGO;
        }
    }
}

[Serializable]
public class PoolConfig {
    public GameObject[] poolPrefabs; // allows pooling of one or several related GOs
    public int poolAmount;
    public List<GameObject> poolList = new List<GameObject>();
    public string poolTag;

    // So client doesnt have to create array just to create a pool with only one prefab!
    public PoolConfig(string poolTag, GameObject poolPrefab, int poolAmount) {
        this.poolPrefabs = new GameObject[1] { poolPrefab };
        this.poolAmount = poolAmount;
        this.poolTag = poolTag;
    }

    public PoolConfig(string poolTag, GameObject[] poolPrefabs, int poolAmount) {
        this.poolPrefabs = poolPrefabs ?? throw new ArgumentNullException(nameof(poolPrefabs));
        this.poolAmount = poolAmount;
        this.poolTag = poolTag ?? throw new ArgumentNullException(nameof(poolTag));
    }
}

