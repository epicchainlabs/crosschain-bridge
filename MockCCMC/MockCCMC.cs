using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.ComponentModel;
using System.Numerics;

// [assembly: ContractTitle("optional contract title")]
// [assembly: ContractDescription("optional contract description")]
// [assembly: ContractVersion("optional contract version")]
// [assembly: ContractAuthor("optional contract author")]
// [assembly: ContractEmail("optional contract email")]
[assembly: Features(ContractPropertyState.HasStorage | ContractPropertyState.HasDynamicInvoke | ContractPropertyState.Payable)]

namespace MockCCMC
{
    public class MockCCMC : SmartContract
    {
        private delegate object DynCall(string method, object[] args); // dynamic call

        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                Runtime.Notify("Test");
                Runtime.Notify(123);
                Runtime.Notify("Verification Trigger");
                return true;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (method == "createCrossChainTx")
                    return CreateCrossChainTx((BigInteger)args[0], (byte[])args[1], (string)args[2], (byte[])args[3]);

                if (method == "callNeoProxy")
                    return CallNeoProxy((BigInteger)args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3]);

                // following methods are for testing purpose
                if (method == "testDeserialize")
                {
                    var x = (byte[])args[0];
                    Runtime.Notify(x.AsString());
                    return DeserializeArgs(x);
                    //return DeserializeArgs((byte[])args[0]);
                }
                if (method == "testSerialize")
                    return SerializeArgs((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
            }

            return false;
        }

        [DisplayName("createCrossChainTx")]
        public static bool CreateCrossChainTx(BigInteger toChainId, byte[] toProxyContract, string methodName, byte[] inputBytes)
        {
            Runtime.Notify(toChainId);
            Runtime.Notify(toProxyContract.AsString());
            Runtime.Notify(methodName);
            var results = DeserializeArgs(inputBytes);
            var assetHash = (byte[])results[0];
            var toAddress = (byte[])results[1];
            var amount = (BigInteger)results[2];
            Runtime.Notify(assetHash.AsString());
            Runtime.Notify(toAddress.AsString());
            Runtime.Notify(amount);
            return true;
        }

        [DisplayName("callNeoProxy")]
        public static bool CallNeoProxy(BigInteger fromChainId, byte[] fromProxyContract, byte[] neoProxyHash, byte[] inputBytes)
        {
            Runtime.Notify(fromChainId);
            Runtime.Notify(fromProxyContract.AsString());
            Runtime.Notify(neoProxyHash.AsString());
            Runtime.Notify(inputBytes.AsString());
            var neo = (DynCall)neoProxyHash.ToDelegate();
            var param = new object[] { inputBytes, fromProxyContract, fromChainId };
            var success = (bool)neo("unlock", param);
            Runtime.Notify(success);
            return true;
        }


        [DisplayName("testSerialize")]
        public static byte[] SerializeArgs(byte[] assetHash, byte[] address, BigInteger amount)
        {
            var buffer = new byte[] { };
            buffer = WriteVarBytes(assetHash, buffer);
            buffer = WriteVarBytes(address, buffer);
            buffer = WriteVarInt(amount, buffer);
            return buffer;
        }

        [DisplayName("testDeserialize")]
        public static object[] DeserializeArgs(byte[] buffer)
        {
            Runtime.Notify(buffer.Length);
            Runtime.Notify(buffer.AsString());
            var offset = 0;
            var res = ReadVarBytes(buffer, offset);
            var assetAddress = res[0];

            res = ReadVarBytes(buffer, (int)res[1]);
            var toAddress = res[0];

            res = ReadVarInt(buffer, (int)res[1]);
            var amount = res[0];

            return new object[] { assetAddress, toAddress, amount };
        }


        // return [BigInteger: value, int: offset]
        private static object[] ReadVarInt(byte[] buffer, int offset)
        {
            var res = ReadBytes(buffer, offset, 1); // read the first byte
            var fb = (byte[])res[0];
            if (fb.Length != 1) throw new ArgumentOutOfRangeException();
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

        private static byte[] WriteVarInt(BigInteger value, byte[] Source)
        {
            if (value < 0)
            {
                return Source;
            }
            else if (value < 0xFD)
            {
                return Source.Concat(value.ToByteArray());
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
                return value;
            for (int i = 0; i < length - l; i++)
            {
                value = value.Concat(new byte[] { 0x00 });
            }
            return value;
        }
    }
}
