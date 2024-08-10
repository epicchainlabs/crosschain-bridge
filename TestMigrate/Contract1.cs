using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.ComponentModel;
using System.Numerics;

namespace TestMigrate
{
    public class Contract1 : SmartContract
    {
        public static object Main(string operation, object[] args)
        {
            Storage.Put("Hello", "World");
            return true;
        }


        [DisplayName("bindProxyHash")]
        public static bool BindProxyHash(BigInteger toChainId, byte[] targetProxyHash)
        {
            StorageMap proxyHash = Storage.CurrentContext.CreateMap(nameof(proxyHash));
            proxyHash.Put(toChainId.AsByteArray(), targetProxyHash);
            return true;
        }

        [DisplayName("getProxyHash")]
        public static byte[] GetProxyHash(BigInteger toChainId)
        {
            StorageMap proxyHash = Storage.CurrentContext.CreateMap(nameof(proxyHash));
            return proxyHash.Get(toChainId.AsByteArray());
        }
    }
}
