import { useT } from "@seliseblocks/genesis-os";
interface LogoProps {
  src?: string;
  alt?: string;
  width?: number;
  height?: number;
  className?: string;
}

export function Logo({ src, alt, width, height, className }: LogoProps) {
  const { t } = useT("logic.shared");
  if (src) {
    return <img src={src} alt={alt ?? t("LOGO.SELISE_LOGO")} width={width} height={height} className={className} />;
  }

  return (
    <>
      <img
        src="/Logo.svg"
        alt={alt ?? t("LOGO.SELISE_LOGO")}
        width={width}
        height={height}
        className={`${className ?? ""} dark:hidden`}
      />
      <img
        src="/Logo_White.svg"
        alt={alt ?? t("LOGO.SELISE_LOGO")}
        width={width}
        height={height}
        className={`${className ?? ""} hidden dark:block`}
      />
    </>
  );
}
