using System;

namespace BMC.Core
{
    public class Singleton<T> where T : Singleton<T>
    {
        private static T instance;
        private static readonly object lockObject = new object();

        protected Singleton()
        {
            // 受保護的建構子，防止外部實例化
        }

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (lockObject)
                    {
                        if (instance == null)
                        {
                            instance = Activator.CreateInstance<T>();
                            instance.Init();
                        }
                    }
                }
                return instance;
            }
        }

        protected virtual void Init() { }
    }
}
