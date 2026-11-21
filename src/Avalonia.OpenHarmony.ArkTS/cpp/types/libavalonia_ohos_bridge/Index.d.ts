export const configureFiles: (filesDir: string) => void;
export const configureInputMethod: (windowId: number) => void;
export const registerSystemRequests: (handler: (id: bigint, operation: string, argument: string) => void,
                                      canPickFolders: boolean) => void;
export const unregisterSystemRequests: () => void;
export const completeSystemRequest: (id: bigint, value: string | null, fd?: number) => void;
export const requestPaste: () => void;
export const setLifecycle: (transition: number) => void;
export const setTheme: (dark: boolean) => void;
export const refreshDisplays: () => void;
export const setWindowPosition: (display: number, x: number, y: number) => void;
export const setSurfaceOffset: (surface: string, x: number, y: number) => void;
export const setAvoidArea: (kind: number, rects: Int32Array) => void;
export const setWindowInsetProperties: (edgeToEdge: boolean, hasColor: boolean, color: number) => void;
export const clearWindowInsets: () => void;
export const requestBack: () => boolean;
export const updateSurface: (surfaceId: string, width: number, height: number, scaling: number) => void;
export const closeSurface: (surfaceId: string) => void;
export const touch: (surfaceId: string, contact: number, phase: number, physicalX: number, physicalY: number) => void;
export const pointer: (surfaceId: string, phase: number, button: number, physicalX: number, physicalY: number,
                       wheelX: number, wheelY: number, modifiers: number) => void;
export const key: (surfaceId: string, down: boolean, code: number, modifiers: number, unicode: number,
                   locks: number) => boolean;
export const inputBlur: (surfaceId: string) => void;
