export const configureFiles: (filesDir: string) => void;
export const configureInputMethod: (windowId: number) => void;
export const registerSystemRequests: (handler: (id: bigint, operation: string, argument: string) => void,
                                      canPickFolders: boolean) => void;
export const unregisterSystemRequests: () => void;
export const completeSystemRequest: (id: bigint, value: string | null, fd?: number) => void;
export const reportTrayEvent: (id: bigint, menuCode: string) => void;
export const requestPaste: () => void;
export const setLifecycle: (transition: number) => void;
export const setTheme: (dark: boolean) => void;
export const refreshDisplays: () => void;
export const startDisplayObservation: () => boolean;
export const stopDisplayObservation: () => void;
export const setWindowPosition: (display: number, x: number, y: number) => void;
export const setSurfaceOffset: (surface: string, x: number, y: number) => void;
export const setAvoidArea: (kind: number, rects: number[]) => void;
export const setChildAvoidArea: (surfaceId: string, kind: number, rects: number[]) => void;
export const setWindowInsetProperties: (edgeToEdge: boolean, hasColor: boolean, color: number) => void;
export const clearWindowInsets: () => void;
export const reportChildFrame: (key: number, windowId: number, displayId: number, x: number, y: number,
                                width: number, height: number) => void;
export const reportChildEvent: (key: number, event: number) => void;
export const supportsTitleBar: () => boolean;
export const reportTitleBar: (extended: boolean, height: number, buttonInset: number) => void;
export const titleBarPress: (surfaceId: string, mouse: boolean, contact: number, x: number, y: number,
  modifiers: number) => number;
export const requestChildClose: (key: number) => Promise<boolean>;
export const requestBack: () => boolean;
export const updateSurface: (surfaceId: string, width: number, height: number, scaling: number,
                             childWindowKey?: number, nativeControls?: NodeContent) => void;
export const closeSurface: (surfaceId: string) => void;
/** Legacy screen coordinates are accepted for compatibility. */
export const configureAccessibility: (surfaceId: string, enabled: boolean, content: NodeContent | null,
                                      legacyScreenX?: number, legacyScreenY?: number) => number;
export const touch: (surfaceId: string, contact: number, phase: number, physicalX: number, physicalY: number) => void;
export const pointer: (surfaceId: string, phase: number, button: number, physicalX: number, physicalY: number,
                       wheelX: number, wheelY: number, modifiers: number) => void;
export const key: (surfaceId: string, down: boolean, code: number, modifiers: number, unicode: number,
                   locks: number) => boolean;
export const inputBlur: (surfaceId: string) => void;
export const drag: (surfaceId: string, phase: number, physicalX: number, physicalY: number, modifiers: number,
                    formats: number, payload: string) => boolean;
import {NodeContent} from '@kit.ArkUI';
