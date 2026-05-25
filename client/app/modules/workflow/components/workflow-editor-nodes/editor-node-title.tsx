type EditorNodeTitleProps = {
  icon: React.ReactNode;
  title: string;
};
export const EditorNodeTitle = ({ icon, title }: EditorNodeTitleProps) => {
  return (
    <div className="flex items-center gap-2.5 font-semibold text-high-emphasis">
      {icon}
      <span>{title}</span>
    </div>
  );
};
