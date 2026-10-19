using System.Runtime.InteropServices;

namespace Meifeng.Core.Engine;

// rime_api.h 的结构体与函数表，字段顺序必须与头文件完全一致
public static unsafe class RimeNative
{
    public const int True = 1;
    public const int False = 0;

    [DllImport("rime", CallingConvention = CallingConvention.Cdecl, EntryPoint = "rime_get_api")]
    public static extern RimeApi* rime_get_api();

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeTraits
    {
        public int data_size;
        public nint shared_data_dir;
        public nint user_data_dir;
        public nint distribution_name;
        public nint distribution_code_name;
        public nint distribution_version;
        public nint app_name;
        public nint modules;
        public int min_log_level;
        public nint log_dir;
        public nint prebuilt_data_dir;
        public nint staging_dir;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeComposition
    {
        public int length;
        public int cursor_pos;
        public int sel_start;
        public int sel_end;
        public nint preedit;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeCandidate
    {
        public nint text;
        public nint comment;
        public nint reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeMenu
    {
        public int page_size;
        public int page_no;
        public int is_last_page;
        public int highlighted_candidate_index;
        public int num_candidates;
        public RimeCandidate* candidates;
        public nint select_keys;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeCommit
    {
        public int data_size;
        public nint text;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeContext
    {
        public int data_size;
        public RimeComposition composition;
        public RimeMenu menu;
        public nint commit_text_preview;
        public nint select_labels;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeStatus
    {
        public int data_size;
        public nint schema_id;
        public nint schema_name;
        public int is_disabled;
        public int is_composing;
        public int is_ascii_mode;
        public int is_full_shape;
        public int is_simplified;
        public int is_traditional;
        public int is_ascii_punct;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RimeCandidateListIterator
    {
        public nint ptr;
        public int index;
        public RimeCandidate candidate;
    }

    // 函数表。未用到的项统一声明为 nint 占位，只保证偏移正确
    [StructLayout(LayoutKind.Sequential)]
    public struct RimeApi
    {
        public int data_size;

        public delegate* unmanaged[Cdecl]<RimeTraits*, void> setup;
        public nint set_notification_handler;

        public delegate* unmanaged[Cdecl]<RimeTraits*, void> initialize;
        public delegate* unmanaged[Cdecl]<void> finalize;

        public delegate* unmanaged[Cdecl]<int, int> start_maintenance;
        public delegate* unmanaged[Cdecl]<int> is_maintenance_mode;
        public delegate* unmanaged[Cdecl]<void> join_maintenance_thread;

        public nint deployer_initialize;
        public nint prebuild;
        public delegate* unmanaged[Cdecl]<int> deploy;
        public nint deploy_schema;
        public nint deploy_config_file;

        public delegate* unmanaged[Cdecl]<int> sync_user_data;

        public delegate* unmanaged[Cdecl]<nuint> create_session;
        public delegate* unmanaged[Cdecl]<nuint, int> find_session;
        public delegate* unmanaged[Cdecl]<nuint, int> destroy_session;
        public nint cleanup_stale_sessions;
        public nint cleanup_all_sessions;

        public delegate* unmanaged[Cdecl]<nuint, int, int, int> process_key;
        public delegate* unmanaged[Cdecl]<nuint, int> commit_composition;
        public delegate* unmanaged[Cdecl]<nuint, void> clear_composition;

        public delegate* unmanaged[Cdecl]<nuint, RimeCommit*, int> get_commit;
        public delegate* unmanaged[Cdecl]<RimeCommit*, int> free_commit;
        public delegate* unmanaged[Cdecl]<nuint, RimeContext*, int> get_context;
        public delegate* unmanaged[Cdecl]<RimeContext*, int> free_context;
        public delegate* unmanaged[Cdecl]<nuint, RimeStatus*, int> get_status;
        public delegate* unmanaged[Cdecl]<RimeStatus*, int> free_status;

        public delegate* unmanaged[Cdecl]<nuint, byte*, int, void> set_option;
        public delegate* unmanaged[Cdecl]<nuint, byte*, int> get_option;

        public nint set_property;
        public nint get_property;

        public nint get_schema_list;
        public nint free_schema_list;

        public nint get_current_schema;
        public delegate* unmanaged[Cdecl]<nuint, byte*, int> select_schema;

        public nint schema_open;
        public nint config_open;
        public nint config_close;
        public nint config_get_bool;
        public nint config_get_int;
        public nint config_get_double;
        public nint config_get_string;
        public nint config_get_cstring;
        public nint config_update_signature;
        public nint config_begin_map;
        public nint config_next;
        public nint config_end;

        public nint simulate_key_sequence;

        public nint register_module;
        public nint find_module;

        public nint run_task;

        public nint get_shared_data_dir;
        public nint get_user_data_dir;
        public nint get_sync_dir;

        public nint get_user_id;
        public nint get_user_data_sync_dir;

        public nint config_init;
        public nint config_load_string;

        public nint config_set_bool;
        public nint config_set_int;
        public nint config_set_double;
        public nint config_set_string;

        public nint config_get_item;
        public nint config_set_item;
        public nint config_clear;
        public nint config_create_list;
        public nint config_create_map;
        public nint config_list_size;
        public nint config_begin_list;

        public delegate* unmanaged[Cdecl]<nuint, byte*> get_input;
        public delegate* unmanaged[Cdecl]<nuint, nuint> get_caret_pos;
        public delegate* unmanaged[Cdecl]<nuint, nuint, int> select_candidate;
        public delegate* unmanaged[Cdecl]<byte*> get_version;
        public delegate* unmanaged[Cdecl]<nuint, nuint, void> set_caret_pos;
        public delegate* unmanaged[Cdecl]<nuint, nuint, int> select_candidate_on_current_page;

        public delegate* unmanaged[Cdecl]<nuint, RimeCandidateListIterator*, int> candidate_list_begin;
        public delegate* unmanaged[Cdecl]<RimeCandidateListIterator*, int> candidate_list_next;
        public delegate* unmanaged[Cdecl]<RimeCandidateListIterator*, void> candidate_list_end;

        public nint user_config_open;
        public nint candidate_list_from_index;

        public nint get_prebuilt_data_dir;
        public nint get_staging_dir;

        public nint commit_proto;
        public nint context_proto;
        public nint status_proto;

        public nint get_state_label;

        public nint delete_candidate;
        public nint delete_candidate_on_current_page;

        public nint get_state_label_abbreviated;

        public nint set_input;

        public nint get_shared_data_dir_s;
        public nint get_user_data_dir_s;
        public nint get_prebuilt_data_dir_s;
        public nint get_staging_dir_s;
        public nint get_sync_dir_s;

        public nint highlight_candidate;
        public nint highlight_candidate_on_current_page;

        public delegate* unmanaged[Cdecl]<nuint, int, int> change_page;
    }

    // 判断函数表里是否带有某个字段（旧版 librime 的函数表更短）
    public static bool Has(RimeApi* api, int fieldOffset)
    {
        return api != null && sizeof(int) + api->data_size > fieldOffset;
    }

    // 按 UTF-8 读取 C 字符串
    public static string? Utf8(nint p) => p == 0 ? null : Marshal.PtrToStringUTF8(p);

    public static string? Utf8(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);
}
