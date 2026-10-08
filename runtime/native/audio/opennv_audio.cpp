#include "gdextension_interface.h"
#include <cstdint>
#include <cstring>
#include <new>

#if defined(_WIN32)
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

// The bridge only adapts Godot's raw mixer ABI. Samples, clocks and saved
// continuation belong to the managed playback owner.
namespace {
struct Name { uint64_t data = 0; };
struct Frame { float left, right; };
using Mix = int32_t (*)(void *, Frame *, float, int32_t);
using Query = double (*)(void *, int32_t, double);
using Allocate = void *(*)(void *);
using Release = void (*)(void *);
struct Instance {
    GDExtensionObjectPtr object = nullptr;
    void *context = nullptr;
    bool stream = false;
    double length = 0;
    bool loop = false;
};
GDExtensionClassLibraryPtr library;
GDExtensionInterfaceClassdbConstructObject3 construct;
GDExtensionInterfaceObjectSetInstance set_instance;
GDExtensionInterfaceObjectDestroy destroy_object;
GDExtensionInterfaceObjectGetInstanceId instance_id;
GDExtensionInterfaceObjectGetInstanceFromId from_id;
GDExtensionInterfaceObjectGetInstanceBinding get_binding;
GDExtensionInterfaceObjectSetInstanceBinding set_binding;
GDExtensionInterfaceRefSetObject set_ref;
GDExtensionInterfaceClassdbGetMethodBind method;
GDExtensionInterfaceObjectMethodBindPtrcall ptrcall;
GDExtensionInterfaceClassdbRegisterExtensionClass6 register_class;
GDExtensionInterfaceClassdbUnregisterExtensionClass unregister_class;
GDExtensionInterfaceStringNameNewWithUtf8Chars new_name;
GDExtensionPtrDestructor destroy_name;
GDExtensionPtrOperatorEvaluator equal_names;
Name stream_class, playback_class, stream_base, playback_base;
Name names[10];
GDExtensionMethodBindPtr notification, unreference;
Mix mix_callback;
Query query_callback;
Allocate allocate_callback;
Release release_callback;

bool equals(GDExtensionConstStringNamePtr name, const Name &expected) {
    GDExtensionBool result = false;
    equal_names(name, &expected, &result);
    return result != 0;
}
void unref(GDExtensionObjectPtr object) {
    GDExtensionBool last_reference = false;
    ptrcall(unreference, object, nullptr, &last_reference);
    // unreference reports deletion eligibility; the caller performs deletion.
    if (last_reference) destroy_object(object);
}
void *binding_create(void *, void *) { return nullptr; }
void binding_free(void *, void *, void *) {}
GDExtensionBool binding_reference(void *, void *, GDExtensionBool) { return true; }
GDExtensionInstanceBindingCallbacks bindings = {binding_create, binding_free, binding_reference};

Instance *make(bool stream, bool notify) {
    auto *instance = new (std::nothrow) Instance;
    if (!instance) return nullptr;
    instance->stream = stream;
    instance->object = construct(stream ? &stream_base : &playback_base);
    if (!instance->object) { delete instance; return nullptr; }
    set_instance(instance->object, stream ? &stream_class : &playback_class, instance);
    set_binding(instance->object, library, instance, &bindings);
    if (notify) {
        int64_t what = 0;
        GDExtensionBool reversed = false;
        const void *arguments[] = {&what, &reversed};
        ptrcall(notification, instance->object, arguments, nullptr);
    }
    return instance;
}
GDExtensionObjectPtr create_stream(void *, GDExtensionBool notify) {
    auto *instance = make(true, notify != 0);
    return instance ? instance->object : nullptr;
}
GDExtensionObjectPtr create_playback(void *, GDExtensionBool notify) {
    auto *instance = make(false, notify != 0);
    return instance ? instance->object : nullptr;
}
void free_instance(void *, void *raw) {
    auto *instance = static_cast<Instance *>(raw);
    if (instance->context && release_callback) release_callback(instance->context);
    delete instance;
}
void instantiate(void *raw, const void *const *, void *result) {
    auto *stream = static_cast<Instance *>(raw);
    auto *playback = make(false, true);
    if (!playback) { set_ref(result, nullptr); return; }
    playback->context = stream->context && allocate_callback ? allocate_callback(stream->context) : nullptr;
    set_ref(result, playback->object);
    unref(playback->object); // The returned Ref now owns the construction reference.
}
void length(void *raw, const void *const *, void *result) {
    *static_cast<double *>(result) = static_cast<Instance *>(raw)->length;
}
void has_loop(void *raw, const void *const *, void *result) {
    *static_cast<GDExtensionBool *>(result) = static_cast<Instance *>(raw)->loop;
}
void start(void *raw, const void *const *args, void *) {
    auto *instance = static_cast<Instance *>(raw);
    if (instance->context) query_callback(instance->context, 0, *static_cast<const double *>(args[0]));
}
void stop(void *raw, const void *const *, void *) {
    auto *instance = static_cast<Instance *>(raw);
    if (instance->context) query_callback(instance->context, 1, 0);
}
void playing(void *raw, const void *const *, void *result) {
    auto *instance = static_cast<Instance *>(raw);
    *static_cast<GDExtensionBool *>(result) = instance->context && query_callback(instance->context, 2, 0) != 0;
}
void position(void *raw, const void *const *, void *result) {
    auto *instance = static_cast<Instance *>(raw);
    *static_cast<double *>(result) = instance->context ? query_callback(instance->context, 3, 0) : 0;
}
void seek(void *raw, const void *const *args, void *) {
    auto *instance = static_cast<Instance *>(raw);
    if (instance->context) query_callback(instance->context, 4, *static_cast<const double *>(args[0]));
}
void loops(void *raw, const void *const *, void *result) {
    auto *instance = static_cast<Instance *>(raw);
    *static_cast<int64_t *>(result) = instance->context ? static_cast<int64_t>(query_callback(instance->context, 5, 0)) : 0;
}
void mix(void *raw, const void *const *args, void *result) {
    auto *instance = static_cast<Instance *>(raw);
    auto *buffer = *static_cast<Frame *const *>(args[0]);
    auto rate = static_cast<float>(*static_cast<const double *>(args[1]));
    auto count = static_cast<int32_t>(*static_cast<const int64_t *>(args[2]));
    *static_cast<int64_t *>(result) = instance->context ? mix_callback(instance->context, buffer, rate, count) : 0;
}
GDExtensionClassCallVirtual stream_virtual(void *, const void *name, uint32_t hash) {
    if (equals(name, names[0]) && hash == 3093715447u) return instantiate;
    if (equals(name, names[1]) && hash == 1740695150u) return length;
    if (equals(name, names[2]) && hash == 36873697u) return has_loop;
    return nullptr;
}
GDExtensionClassCallVirtual playback_virtual(void *, const void *name, uint32_t hash) {
    if (equals(name, names[3]) && hash == 373806689u) return start;
    if (equals(name, names[4]) && hash == 3218959716u) return stop;
    if (equals(name, names[5]) && hash == 36873697u) return playing;
    if (equals(name, names[6]) && hash == 1740695150u) return position;
    if (equals(name, names[7]) && hash == 373806689u) return seek;
    if (equals(name, names[8]) && hash == 3905245786u) return loops;
    if (equals(name, names[9]) && hash == 925936155u) return mix;
    return nullptr;
}
void initialize(void *, GDExtensionInitializationLevel level) {
    if (level != GDEXTENSION_INITIALIZATION_SCENE) return;
    new_name(&stream_class, "OpenNVPcmStream"); new_name(&playback_class, "OpenNVPcmPlayback");
    new_name(&stream_base, "AudioStream"); new_name(&playback_base, "AudioStreamPlayback");
    const char *methods[] = {"_instantiate_playback", "_get_length", "_has_loop", "_start", "_stop",
        "_is_playing", "_get_playback_position", "_seek", "_get_loop_count", "_mix"};
    for (int i = 0; i < 10; ++i) new_name(&names[i], methods[i]);
    Name object_name, notification_name, ref_name, unref_name;
    new_name(&object_name, "Object"); new_name(&notification_name, "notification");
    new_name(&ref_name, "RefCounted"); new_name(&unref_name, "unreference");
    notification = method(&object_name, &notification_name, 4023243586);
    unreference = method(&ref_name, &unref_name, 2240911060);
    destroy_name(&object_name); destroy_name(&notification_name); destroy_name(&ref_name); destroy_name(&unref_name);
    GDExtensionClassCreationInfo6 stream_info = {};
    stream_info.is_exposed = true; stream_info.create_instance_func = create_stream;
    stream_info.free_instance_func = free_instance; stream_info.get_virtual_func = stream_virtual;
    register_class(library, &stream_class, &stream_base, &stream_info);
    GDExtensionClassCreationInfo6 playback_info = {};
    playback_info.is_exposed = true; playback_info.create_instance_func = create_playback;
    playback_info.free_instance_func = free_instance; playback_info.get_virtual_func = playback_virtual;
    register_class(library, &playback_class, &playback_base, &playback_info);
}
void deinitialize(void *, GDExtensionInitializationLevel level) {
    if (level != GDEXTENSION_INITIALIZATION_SCENE) return;
    unregister_class(library, &playback_class); unregister_class(library, &stream_class);
    for (auto &name : names) destroy_name(&name);
    destroy_name(&stream_class); destroy_name(&playback_class); destroy_name(&stream_base); destroy_name(&playback_base);
}
}

EXPORT uint64_t opennv_audio_create(void *context, Mix pcm_mix, Query query, Allocate allocate, Release release,
    double seconds, int32_t loop) {
    if (!context || !pcm_mix || !query || !allocate || !release || !notification) return 0;
    mix_callback = pcm_mix; query_callback = query; allocate_callback = allocate; release_callback = release;
    auto *stream = make(true, true);
    if (!stream) return 0;
    stream->context = context; stream->length = seconds; stream->loop = loop != 0;
    return instance_id(stream->object);
}
EXPORT void opennv_audio_release(uint64_t id) {
    if (auto *object = from_id(id)) unref(object);
}
EXPORT GDExtensionBool opennv_audio_init(GDExtensionInterfaceGetProcAddress get,
    GDExtensionClassLibraryPtr handle, GDExtensionInitialization *init) {
    library = handle;
#define LOAD(variable, type, name) variable = reinterpret_cast<type>(get(name)); if (!variable) return false
    LOAD(construct, GDExtensionInterfaceClassdbConstructObject3, "classdb_construct_object3");
    LOAD(set_instance, GDExtensionInterfaceObjectSetInstance, "object_set_instance");
    LOAD(destroy_object, GDExtensionInterfaceObjectDestroy, "object_destroy");
    LOAD(instance_id, GDExtensionInterfaceObjectGetInstanceId, "object_get_instance_id");
    LOAD(from_id, GDExtensionInterfaceObjectGetInstanceFromId, "object_get_instance_from_id");
    LOAD(get_binding, GDExtensionInterfaceObjectGetInstanceBinding, "object_get_instance_binding");
    LOAD(set_binding, GDExtensionInterfaceObjectSetInstanceBinding, "object_set_instance_binding");
    LOAD(set_ref, GDExtensionInterfaceRefSetObject, "ref_set_object");
    LOAD(method, GDExtensionInterfaceClassdbGetMethodBind, "classdb_get_method_bind");
    LOAD(ptrcall, GDExtensionInterfaceObjectMethodBindPtrcall, "object_method_bind_ptrcall");
    LOAD(register_class, GDExtensionInterfaceClassdbRegisterExtensionClass6, "classdb_register_extension_class6");
    LOAD(unregister_class, GDExtensionInterfaceClassdbUnregisterExtensionClass, "classdb_unregister_extension_class");
    LOAD(new_name, GDExtensionInterfaceStringNameNewWithUtf8Chars, "string_name_new_with_utf8_chars");
    auto destructor = reinterpret_cast<GDExtensionInterfaceVariantGetPtrDestructor>(get("variant_get_ptr_destructor"));
    auto evaluator = reinterpret_cast<GDExtensionInterfaceVariantGetPtrOperatorEvaluator>(get("variant_get_ptr_operator_evaluator"));
    if (!destructor || !evaluator) return false;
    destroy_name = destructor(GDEXTENSION_VARIANT_TYPE_STRING_NAME);
    equal_names = evaluator(GDEXTENSION_VARIANT_OP_EQUAL, GDEXTENSION_VARIANT_TYPE_STRING_NAME, GDEXTENSION_VARIANT_TYPE_STRING_NAME);
    init->minimum_initialization_level = GDEXTENSION_INITIALIZATION_SCENE;
    init->initialize = initialize; init->deinitialize = deinitialize; init->userdata = nullptr;
    return true;
}
