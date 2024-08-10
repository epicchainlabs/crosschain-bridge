using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

// [assembly: ContractTitle("optional contract title")]
// [assembly: ContractDescription("optional contract description")]
// [assembly: ContractVersion("optional contract version")]
// [assembly: ContractAuthor("optional contract author")]
// [assembly: ContractEmail("optional contract email")]
[assembly: Features(ContractPropertyState.HasStorage | ContractPropertyState.HasDynamicInvoke | ContractPropertyState.Payable)]

namespace Nep5Proxy
{
    public class Nep5Proxy : SmartContract
    {
        //[Appcall("50f8b57cccfc4eaf635e1fae9466b650b6958a2a")] // CCMC scriptHash
        //public static extern object CCMC(string method, object[] args);

        // Constants
        private static readonly byte[] CCMCScriptHash = "".HexToBytes();
        private static readonly byte[] Operator = "".ToScriptHash(); // Operator address

        // Dynamic Call
        private delegate object DynCall(string method, object[] args); // dynamic call
        
        // Events
        public static event Action<byte[], BigInteger, byte[], byte[], byte[], BigInteger> LockEvent;
        public static event Action<byte[], byte[], BigInteger> UnlockEvent;
        public static event Action<byte[], BigInteger, byte[], BigInteger> BindAssetHashEvent;
        public static event Action<BigInteger, byte[]> BindProxyHashEvent;

        // ---------------------StorageMap key definitions----------------------------
        // StorageMap proxyHash, key: toChainId, value: byte[]
        // StorageMap assetHash, key: fromAssetHash + toChainId, value: byte[]
        // StorageMap lockedAmount, key: fromAssetHash, value: BigInteger
        // ---------------------------------------------------------------------------

        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                if (method == "bindProxyHash")
                    return BindProxyHash((BigInteger)args[0], (byte[])args[1]);
                if (method == "bindAssetHash")
                    return BindAssetHash((byte[])args[0], (BigInteger)args[1], (byte[])args[2], (BigInteger)args[3]);
                if (method == "getAssetBalance")
                    return GetAssetBalance((byte[])args[0]);
                if (method == "getProxyHash")
                    return GetProxyHash((BigInteger)args[0]);
                if (method == "getAssetHash")
                    return GetAssetHash((byte[])args[0], (BigInteger)args[1]);
                if (method == "lock")
                    return Lock((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3], (BigInteger)args[4]);
                if (method == "unlock")
                    return Unlock((byte[])args[0], (byte[])args[1], (BigInteger)args[2], callscript);
                if (method == "getLockedAmount")
                    return GetLockedAmount((byte[])args[0]);
                
                if (method == "upgrade")
                {
                    Runtime.Notify("In upgrade");
                    if (args.Length < 9) return false;
                    byte[] script = (byte[])args[0];
                    byte[] plist = (byte[])args[1];
                    byte rtype = (byte)args[2];
                    ContractPropertyState cps = (ContractPropertyState)args[3];
                    string name = (string)args[4];
                    string version = (string)args[5];
                    string author = (string)args[6];
                    string email = (string)args[7];
                    string description = (string)args[8];
                    return Upgrade(script, plist, rtype, cps, name, version, author, email, description);
                }
                // following methods are for testing only
                //if (method == "testDynCall")
                //    return TestDynCall((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                //if (method == "testDynCall2")
                //    return TestDynCall2((byte[])args[0], (BigInteger)args[1], (byte[])args[2], (string)args[3], (byte[])args[4]);
                //if (method == "testDeserialize")
                //{
                //    var x = (byte[])args[0];
                //    Runtime.Notify(x);
                //    return DeserializeArgs(x);
                //}
                //if (method == "testSerialize")
                //    return SerializeArgs((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
            }
            return false;
        }

        // add target proxy contract hash according to chain id into contract storage
        [DisplayName("bindProxyHash")]
        public static bool BindProxyHash(BigInteger toChainId, byte[] targetProxyHash)
        {
            if (!Runtime.CheckWitness(Operator)) return false;
            StorageMap proxyHash = Storage.CurrentContext.CreateMap(nameof(proxyHash));
            proxyHash.Put(toChainId.AsByteArray(), targetProxyHash);
            BindProxyHashEvent(toChainId, targetProxyHash);
            return true;
        }

        // add target asset contract hash according to local asset hash & chain id into contract storage
        [DisplayName("bindAssetHash")]
        public static bool BindAssetHash(byte[] fromAssetHash, BigInteger toChainId, byte[] toAssetHash, BigInteger initialAmount)
        {
            if (!Runtime.CheckWitness(Operator)) return false;
            StorageMap assetHash = Storage.CurrentContext.CreateMap(nameof(assetHash));
            assetHash.Put(fromAssetHash.Concat(toChainId.AsByteArray()), toAssetHash);
            
            if (GetAssetBalance(fromAssetHash) != initialAmount)
            {
                Runtime.Notify("Initial amount incorrect.");
                return false;
            }

            StorageMap lockedAmount = Storage.CurrentContext.CreateMap(nameof(lockedAmount));
            lockedAmount.Put(fromAssetHash, initialAmount);
            BindAssetHashEvent(fromAssetHash, toChainId, toAssetHash, initialAmount);
            return true;
        }

        [DisplayName("getAssetBalance")]
        public static BigInteger GetAssetBalance(byte[] assetHash)
        {
            byte[] currentHash = ExecutionEngine.ExecutingScriptHash; // this proxy contract hash
            var nep5Contract = (DynCall)assetHash.ToDelegate();
            BigInteger balance = (BigInteger)nep5Contract("balanceOf", new object[] { currentHash });
            return balance;
        }

        // get target proxy contract hash according to chain id
        [DisplayName("getProxyHash")]
        public static byte[] GetProxyHash(BigInteger toChainId)
        {
            StorageMap proxyHash = Storage.CurrentContext.CreateMap(nameof(proxyHash));
            return proxyHash.Get(toChainId.AsByteArray());
        }

        // get target asset contract hash according to local asset hash & chain id
        [DisplayName("getAssetHash")]
        public static byte[] GetAssetHash(byte[] fromAssetHash, BigInteger toChainId)
        {
            StorageMap assetHash = Storage.CurrentContext.CreateMap(nameof(assetHash));
            return assetHash.Get(fromAssetHash.Concat(toChainId.AsByteArray()));
        }

        // used to lock asset into proxy contract
        [DisplayName("lock")]
        public static bool Lock(byte[] fromAssetHash, byte[] fromAddress, BigInteger toChainId, byte[] toAddress, BigInteger amount)
        {
            // check parameters
            if (fromAssetHash.Length != 20)
            {
                Runtime.Notify("The parameter fromAssetHash SHOULD be 20-byte long.");
                return false;
            }
            if (fromAddress.Length != 20)
            {
                Runtime.Notify("The parameter fromAddress SHOULD be 20-byte long.");
                return false;
            }
            if (toAddress.Length == 0)
            {
                Runtime.Notify("The parameter toAddress SHOULD not be empty.");
                return false;
            }
            if (amount < 0)
            {
                Runtime.Notify("The parameter amount SHOULD not be less than 0.");
                return false;
            }
            
            // get the corresbonding asset on target chain
            var toAssetHash = GetAssetHash(fromAssetHash, toChainId);
            if (toAssetHash.Length == 0)
            {
                Runtime.Notify("Target chain asset hash not found.");
                return false;
            }

            // get the proxy contract on target chain
            var toContract = GetProxyHash(toChainId);
            if (toContract.Length == 0)
            {
                Runtime.Notify("Target chain proxy contract not found.");
                return false;
            }

            // transfer asset from fromAddress to proxy contract address, use dynamic call to call nep5 token's contract "transfer"
            byte[] currentHash = ExecutionEngine.ExecutingScriptHash; // this proxy contract hash
            var nep5Contract = (DynCall)fromAssetHash.ToDelegate();
            bool success = (bool)nep5Contract("transfer", new object[] { fromAddress, currentHash, amount });
            if (!success)
            {
                Runtime.Notify("Failed to transfer NEP5 token to proxy contract.");
                return false;
            }

            // construct args for proxy contract on target chain
            var inputBytes = SerializeArgs(toAssetHash, toAddress, amount);
            // constrct params for CCMC 
            var param = new object[] { toChainId, toContract, "unlock", inputBytes };
            // dynamic call CCMC
            var ccmc = (DynCall)CCMCScriptHash.ToDelegate();
            success = (bool)ccmc("CrossChain", param);
            if (!success)
            {
                Runtime.Notify("Failed to call CCMC.");
                return false;
            }

            // update locked amount
            StorageMap lockedAmount = Storage.CurrentContext.CreateMap(nameof(lockedAmount));
            BigInteger old = lockedAmount.Get(fromAssetHash).ToBigInteger();
            lockedAmount.Put(fromAssetHash, old + amount);

            LockEvent(fromAssetHash, toChainId, toAssetHash, fromAddress, toAddress, amount);

            return true;
        }

#if DEBUG
        [DisplayName("unlock")] //Only for ABI file
        public static bool Unlock(byte[] inputBytes, byte[] fromProxyContract, BigInteger fromChainId) => true;
#endif

        // Methods of actual execution
        // used to unlock asset from proxy contract
        private static bool Unlock(byte[] inputBytes, byte[] fromProxyContract, BigInteger fromChainId, byte[] caller)
        {
            //only allowed to be called by CCMC
            if (caller.AsBigInteger() != CCMCScriptHash.AsBigInteger())
            {
                Runtime.Notify("Only allowed to be called by CCMC");
                return false;
            }

            byte[] storedProxy = GetProxyHash(fromChainId);
            
            // check the fromContract is stored, so we can trust it
            if (fromProxyContract.AsBigInteger() != storedProxy.AsBigInteger())
            {
                Runtime.Notify(fromProxyContract);
                Runtime.Notify(fromChainId);
                Runtime.Notify(storedProxy);
                Runtime.Notify("From proxy contract not found.");
                return false;
            }

            // parse the args bytes constructed in source chain proxy contract, passed by multi-chain
            object[] results = DeserializeArgs(inputBytes);
            var assetHash = (byte[])results[0];
            var toAddress = (byte[])results[1];
            var amount = (BigInteger)results[2];
            if (assetHash.Length != 20) 
            { 
                Runtime.Notify("ToChain Asset script hash SHOULD be 20-byte long.");
                return false; 
            }
            if (toAddress.Length != 20)
            {
                Runtime.Notify("ToChain Account address SHOULD be 20-byte long.");
                return false;
            }
            if (amount < 0)
            {
                Runtime.Notify("ToChain Amount SHOULD not be less than 0.");
                return false;
            }

            // transfer asset from proxy contract to toAddress
            byte[] currentHash = ExecutionEngine.ExecutingScriptHash; // this proxy contract hash
            var nep5Contract = (DynCall)assetHash.ToDelegate();
            bool success = (bool)nep5Contract("transfer", new object[] { currentHash, toAddress, amount });
            if (!success)
            {
                Runtime.Notify("Failed to transfer NEP5 token to toAddress.");
                return false;
            }

            // update locked amount
            StorageMap lockedAmount = Storage.CurrentContext.CreateMap(nameof(lockedAmount));
            BigInteger old = lockedAmount.Get(assetHash).ToBigInteger();
            lockedAmount.Put(assetHash, old - amount);

            UnlockEvent(assetHash, toAddress, amount);

            return true;
        }

        // get target chain circulating supply
        [DisplayName("getLockedAmount")]
        public static BigInteger GetLockedAmount(byte[] fromAssetHash)
        {
            StorageMap lockedAmount = Storage.CurrentContext.CreateMap(nameof(lockedAmount));
            BigInteger locked = lockedAmount.Get(fromAssetHash).ToBigInteger();
            return locked;
        }
        
        // used to upgrade this proxy contract
        [DisplayName("upgrade")]
        public static bool Upgrade(byte[] newScript, byte[] paramList, byte returnType, ContractPropertyState cps, 
            string name, string version, string author, string email, string description)
        {
            if (!Runtime.CheckWitness(Operator)) return false;
            var contract = Contract.Migrate(newScript, paramList, returnType, cps, name, version, author, email, description);
            Runtime.Notify("Proxy contract upgraded");
            return true;
        }

        //[DisplayName("testDynCall")]
        //public static bool TestDynCall(byte[] fromAssetHash, byte[] fromAddress, BigInteger amount)
        //{
        //    byte[] currentHash = ExecutionEngine.ExecutingScriptHash; // this proxy contract hash
        //    var nep5Contract = (DynCall)fromAssetHash.ToDelegate();
        //    bool success = (bool)nep5Contract("transfer", new object[] { fromAddress, currentHash, amount });
        //    return success;
        //}

        //[DisplayName("testDynCall2")]
        //public static bool TestDynCall2(byte[] ccmcHash, BigInteger toChainId, byte[] toProxyContract, string methodName, byte[] inputBytes)
        //{
        //    var param = new object[] { toChainId, toProxyContract, "unlock", inputBytes };
        //    // dynamic call CCMC
        //    var ccmc = (DynCall)ccmcHash.ToDelegate();
        //    var success = (bool)ccmc("createCrossChainTx", param);
        //    return success;
        //}


        #region For Deserialization
        //[DisplayName("testDeserialize")]
        private static object[] DeserializeArgs(byte[] buffer)
        {
            var offset = 0;
            var res = ReadVarBytes(buffer, offset);
            var assetAddress = res[0];

            res = ReadVarBytes(buffer, (int)res[1]);
            var toAddress = res[0];

            res = ReadUint255(buffer, (int)res[1]);
            var amount = res[0];

            return new object[] { assetAddress, toAddress, amount };
        }

        private static object[] ReadUint255(byte[] buffer, int offset)
        {
            if (offset + 32 > buffer.Length)
            {
                Runtime.Notify("Length is not long enough");
                return new object[] { 0, -1 };
            }
            return new object[] { buffer.Range(offset, 32).ToBigInteger(), offset + 32 };
        }

        // return [BigInteger: value, int: offset]
        private static object[] ReadVarInt(byte[] buffer, int offset)
        {
            var res = ReadBytes(buffer, offset, 1); // read the first byte
            var fb = (byte[])res[0];
            if (fb.Length != 1)
            {
                Runtime.Notify("Wrong length");
                return new object[] { 0, -1 };
            }
            var newOffset = (int)res[1];
            if (fb == new byte[] { 0xFD })
            {
                return new object[] { buffer.Range(newOffset, 2).ToBigInteger(), newOffset + 2 };
            }
            else if (fb == new byte[] { 0xFE })
            {
                return new object[] { buffer.Range(newOffset, 4).ToBigInteger(), newOffset + 4 };
            }
            else if (fb == new byte[] { 0xFF })
            {
                return new object[] { buffer.Range(newOffset, 8).ToBigInteger(), newOffset + 8 };
            }
            else
            {
                return new object[] { fb.ToBigInteger(), newOffset };
            }
        }

        // return [byte[], new offset]
        private static object[] ReadVarBytes(byte[] buffer, int offset)
        {
            var res = ReadVarInt(buffer, offset);
            var count = (int)res[0];
            var newOffset = (int)res[1];
            return ReadBytes(buffer, newOffset, count);
        }

        // return [byte[], new offset]
        private static object[] ReadBytes(byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
            return new object[] { buffer.Range(offset, count), offset + count };
        }
        #endregion

        #region For Serialization
        //[DisplayName("testSerialize")]
        private static byte[] SerializeArgs(byte[] assetHash, byte[] address, BigInteger amount)
        {
            var buffer = new byte[] { };
            buffer = WriteVarBytes(assetHash, buffer);
            buffer = WriteVarBytes(address, buffer);
            buffer = WriteUint255(amount, buffer);
            return buffer;
        }

        private static byte[] WriteUint255(BigInteger value, byte[] source)
        {
            if (value < 0)
            {
                Runtime.Notify("Value out of range of uint255");
                return source;
            }
            var v = PadRight(value.ToByteArray(), 32);
            return source.Concat(v); // no need to concat length, fix 32 bytes
        }

        private static byte[] WriteVarInt(BigInteger value, byte[] Source)
        {
            if (value < 0)
            {
                return Source;
            }
            else if (value < 0xFD)
            {
                var v = PadRight(value.ToByteArray(), 1);
                return Source.Concat(v);
            }
            else if (value <= 0xFFFF) // 0xff, need to pad 1 0x00
            {
                byte[] length = new byte[] { 0xFD };
                var v = PadRight(value.ToByteArray(), 2);
                return Source.Concat(length).Concat(v);
            }
            else if (value <= 0XFFFFFFFF) //0xffffff, need to pad 1 0x00 
            {
                byte[] length = new byte[] { 0xFE };
                var v = PadRight(value.ToByteArray(), 4);
                return Source.Concat(length).Concat(v);
            }
            else //0x ff ff ff ff ff, need to pad 3 0x00
            {
                byte[] length = new byte[] { 0xFF };
                var v = PadRight(value.ToByteArray(), 8);
                return Source.Concat(length).Concat(v);
            }
        }

        private static byte[] WriteVarBytes(byte[] value, byte[] Source)
        {
            return WriteVarInt(value.Length, Source).Concat(value); 
        }

        // add padding zeros on the right
        private static byte[] PadRight(byte[] value, int length)
        {
            var l = value.Length;
            if (l > length)
                return value.Range(0, length);
            for (int i = 0; i < length - l; i++)
            {
                value = value.Concat(new byte[] { 0x00 });
            }
            return value;
        }
        #endregion
    }
}
