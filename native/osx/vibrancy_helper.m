#define PLATFORM_OSX
#include <Foundation/Foundation.h>
#include <AppKit/AppKit.h>
#include <CoreServices/CoreServices.h>
#include <objc/objc-runtime.h>

#define VH_MAX_VIEWS 1024

static int32_t s_id_arr[VH_MAX_VIEWS];
static NSVisualEffectView* s_view_arr[VH_MAX_VIEWS];
static int32_t s_arr_size;
static int32_t s_next_id;

void vh_init() {
    s_arr_size = 0;
    s_next_id = 0;
    memset(s_id_arr, -1, sizeof(s_id_arr));
    memset(s_view_arr, 0, sizeof(s_view_arr));
}

int32_t vh_add_view(NSView** ppview) {
    NSView* view = *ppview;
    if(!view) {
        return -1;
    }

    if(s_arr_size >= VH_MAX_VIEWS) {
        return -2;
    }

    NSVisualEffectView* vibrantView = [ 
        [NSVisualEffectView alloc] 
            initWithFrame: [[view window] frame]];

    [vibrantView setBlendingMode:       NSVisualEffectBlendingModeBehindWindow];
    [vibrantView setAutoresizingMask:   NSViewWidthSizable|NSViewHeightSizable];
    [vibrantView setMaterial:           NSVisualEffectMaterialUnderWindowBackground];
    [view.window.contentView
        addSubview: vibrantView
        positioned: NSWindowBelow
        relativeTo: nil];

    int32_t viewId = s_next_id++;
    s_id_arr[s_arr_size] = viewId;
    s_view_arr[s_arr_size] = vibrantView;
    ++s_arr_size;

    return viewId;
}
