

using uint8_t = System.Byte;
using uint16_t = System.UInt16;
using uint32_t = System.UInt32;
using uint64_t = System.UInt64;

using int8_t = System.SByte;
using int16_t = System.Int16;
using int32_t = System.Int32;
using int64_t = System.Int64;

using float32 = System.Single;

using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace DroneCAN
{
    public partial class DroneCAN {



//using uavcan.protocol.file.Error.cs


        public const int UAVCAN_PROTOCOL_FILE_DELETE_RES_MAX_PACK_SIZE = 2;
        public const ulong UAVCAN_PROTOCOL_FILE_DELETE_RES_DT_SIG = 0x78648C99170B47AA;

        public const int UAVCAN_PROTOCOL_FILE_DELETE_RES_DT_ID = 47;






        public partial class uavcan_protocol_file_Delete_res: IDroneCANSerialize {



            public uavcan_protocol_file_Error error = new uavcan_protocol_file_Error();




            public void encode(dronecan_serializer_chunk_cb_ptr_t chunk_cb, object ctx)
            {
                encode_uavcan_protocol_file_Delete_res(this, chunk_cb, ctx);
            }

            public void decode(CanardRxTransfer transfer)
            {
                decode_uavcan_protocol_file_Delete_res(transfer, this);
            }
        }
    }
}
