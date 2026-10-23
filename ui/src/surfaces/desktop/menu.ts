/** One entry of the in-surface context menu (ContextMenu.svelte). */
export interface MenuItem {
  id: string;
  label: string;
  /** radio-style check mark (size choices etc.); undefined = plain item */
  checked?: boolean;
  danger?: boolean;
  disabled?: boolean;
  separator?: boolean;
}
