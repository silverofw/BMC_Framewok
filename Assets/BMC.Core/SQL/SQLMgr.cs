using Cysharp.Threading.Tasks;
using SQLite;
using System;
using System.Collections.Generic;

namespace BMC.Core
{
    public class SQLMgr : Singleton<SQLMgr> 
    {
        public Dictionary<string, SQLiteAsyncConnection> AsyncConns = new();

        public void Clear()
        {
            foreach (var item in AsyncConns)
            {
                Log.Info($"[SQLMgr][{item.Key}] Database Close");
                item.Value.CloseAsync();
            }
            AsyncConns.Clear();
            GC.Collect(); // 需要呼叫才會立刻清除DB引用
            Log.Info($"[SQLMgr] Clear");
        }

        /// <summary>
        /// 建立連線，沒檔案時CreateTableAsync會自動建立
        /// </summary>
        /// <param name="path"></param>
        public void InitAsyncConns(string path)
        {
            Log.Info($"[SQLMgr][InitAsyncConns][{path}]");
            if (AsyncConns.ContainsKey(path))
                return;
            AsyncConns.Add(path, new SQLiteAsyncConnection(path));
        }

        /// <summary>
        /// TODO: 切換地圖時要處理那些連線要保留問題 
        /// </summary>
        /// <param name="key"></param>
        public void CloseAsyncConn(string key)
        {
            if (!AsyncConns.ContainsKey(key))
                return;

            //Log.SEND($"[SQLMgr] Close Conn {key}");
            AsyncConns[key].CloseAsync();
            AsyncConns.Remove(key);
        }
        /// <summary>
        /// 建立Table表格，可除重複呼叫，不會影響舊有資料
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public async UniTask<CreateTableResult> CreateTableAsync<T>(string key) where T : new()
        { 
            try
            {
                return await AsyncConns[key].CreateTableAsync<T>();
            }
            catch (SQLiteException ex)
            {
                Log.Error($"failed: {ex.Message}");
                return default;
            }
        }

        public bool Check(string key)
        {
            if (AsyncConns.ContainsKey(key))
                return true;
            return false;
        }

        public async UniTask<int> Insert(string key, object obj)
        {
            try
            {
                return await AsyncConns[key].InsertAsync(obj);
            }
            catch (SQLiteException ex)
            {
                Log.Error($"failed: {ex.Message}");
                return default;
            }
        }

        public async UniTask<T> Find<T>(string key, object pk) where T : new()
        {
            try
            {
                return await AsyncConns[key].FindAsync<T>(pk);
            }
            catch (SQLiteException ex)
            {
                Log.Error($"[{key}] failed: {ex.Message}");
                return default;
            }
        }

        public async UniTask<int> InsertOrReplace(string key, object obj)
        {
            try
            {
                return await AsyncConns[key].InsertOrReplaceAsync(obj);
            }
            catch (SQLiteException ex)
            {
                Log.Error($"failed: {ex.Message}");
                return default;
            }
        }

        public async UniTask<int> Delete<T>(string key, object pk) where T : new()
        {
            try
            {
                return await AsyncConns[key].DeleteAsync<T>(pk);
            }
            catch (SQLiteException ex)
            {
                Log.Error($"failed: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 重建表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async void RecreateTableAsync<T>(string key) where T : new()
        {
            try
            {
                var tableName = typeof(T).Name;

                // 刪除表
                await AsyncConns[key].ExecuteAsync($"DROP TABLE IF EXISTS {tableName}");
                Log.Info($"Table {tableName} dropped.");

                // 重建表
                await CreateTableAsync<T>(key);
                Log.Info($"Table {tableName} recreated.");
            }
            catch (SQLiteException ex)
            {
                Log.Error($"Failed to recreate table: {ex.Message}");
            }
        }

        /// <summary>
        /// 查詢表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="query">條件</param>
        /// <param name="args"></param>
        /// <returns></returns>
        public async UniTask<List<T>> QueryAsync<T>(string key, string query, params object[] args) where T : new()
        {
            try
            {
                var results = await AsyncConns[key].QueryAsync<T>(query, args);

                //Log.SEND($"[SQLMgr] Found {results.Count} records in range.");
                return results;
            }
            catch (SQLiteException ex)
            {
                Log.Error($"[SQLMgr][{key}] Failed to find in range: {ex.Message}");
                return default;
            }
        }
    }
}