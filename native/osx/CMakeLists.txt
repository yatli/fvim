PROJECT(fvim-ext)
FIND_LIBRARY(COCOA_LIBRARY Cocoa)

SET(src vibrancy_helper.m)

ADD_LIBRARY(fvim-ext SHARED ${src})
TARGET_LINK_LIBRARIES(fvim-ext ${COCOA_LIBRARY})
