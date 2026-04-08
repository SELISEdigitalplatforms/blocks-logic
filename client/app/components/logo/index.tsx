interface LogoProps {
  src?: string;
  alt?: string;
  width?: number;
  height?: number;
  className?: string;
}

export function Logo({ src, alt, width, height, className }: LogoProps) {
  const imgSrc = src || "/Logo.svg";
  return (
    <img
      src={imgSrc}
      alt={alt ?? "SELISE Logo"}
      width={width}
      height={height}
      className={className}
    />
  );
}
