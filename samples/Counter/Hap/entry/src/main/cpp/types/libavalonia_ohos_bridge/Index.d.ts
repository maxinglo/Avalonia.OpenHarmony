export const configureFiles: (filesDir: string) => void;
export const configureInputMethod: (windowId: number) => void;
export const updateSurface: (surfaceId: string, width: number, height: number, scaling: number) => void;
export const closeSurface: (surfaceId: string) => void;
export const touch: (surfaceId: string, contact: number, phase: number, physicalX: number, physicalY: number) => void;
