export const configureFiles: (filesDir: string) => void;
export const configureInputMethod: (windowId: number) => void;
export const registerSystemRequests: (handler: (id: bigint, operation: string, argument: string) => void) => void;
export const unregisterSystemRequests: () => void;
export const completeSystemRequest: (id: bigint, value: string | null, fd?: number) => void;
export const updateSurface: (surfaceId: string, width: number, height: number, scaling: number) => void;
export const closeSurface: (surfaceId: string) => void;
export const touch: (surfaceId: string, contact: number, phase: number, physicalX: number, physicalY: number) => void;
export const pointer: (surfaceId: string, phase: number, button: number, physicalX: number, physicalY: number,
                       wheelX: number, wheelY: number, modifiers: number) => void;
export const key: (surfaceId: string, down: boolean, code: number, modifiers: number, unicode: number,
                   locks: number) => boolean;
export const inputBlur: (surfaceId: string) => void;
