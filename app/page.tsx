import Image from "next/image"
import logotypeMonoLight from "@/public/ixi-logotype-mono-lm.svg"
import logotypeMono from "@/public/ixi-logotype-mono.svg"
import { whitepaper, whitepaperVersion } from "@/settings/settings"
import {
  IoArrowForwardCircleOutline,
  IoDocumentTextOutline,
} from "react-icons/io5"

import CustomCard from "@/components/CustomCard/CustomCard"
import TextElement from "@/components/TextElement/TextElement"

export default function Home() {
  return (
    <div className="min-h-[86.5vh] flex flex-col justify-center items-center text-center px-4 py-16 gap-12 max-[425px]:gap-8">
      <div className="flex flex-col items-center gap-4 max-[425px]:items-start max-[425px]:text-left mb-8">
        <Image
          src={logotypeMono}
          priority
          alt={"ixian-logo"}
          height={40}
          className="max-sm:max-w-40 mb-5 hidden dark:block"
          decoding="async"
        />
        <Image
          src={logotypeMonoLight}
          priority
          alt={"ixian-logo"}
          height={40}
          className="max-sm:max-w-40 mb-5 block dark:hidden"
          decoding="async"
        />
        <TextElement type={"heading-lg"}>
          Build the Future of Decentralized Applications
        </TextElement>
        <TextElement type={"body-lg"}>
          Post-quantum secure, truly peer-to-peer, zero infrastructure costs.
          Welcome to Ixian - where decentralization isn&apos;t just marketing.
        </TextElement>
      </div>
      <div className="w-full flex items-center justify-center">
        <CustomCard
          linkClassName={"whitepaperCardLink"}
          targetBlank
          href={`${whitepaper}`}
          title={"Ixian Platform Whitepaper"}
          description={`Read the Ixian Platform Whitepaper ${whitepaperVersion}`}
          icon={<IoDocumentTextOutline size={48} />}
        />
      </div>
      <div className="homeCardGrid">
        <CustomCard
          title="Node Operator Guides"
          description={
            "Learn how to install, configure, and maintain the different types of nodes that power the Ixian network."
          }
          icon={<IoArrowForwardCircleOutline size={48} />}
          href="/docs/operators"
        />
        <CustomCard
          title="Developer Guides"
          description="Build serverless P2P apps, AI chatbots without API costs, and IoT devices with zero cloud bills."
          href="/docs/developers"
          icon={<IoArrowForwardCircleOutline size={48} />}
        />
        <CustomCard
          title="Architecture"
          description="Explore the fundamental concepts, data structures, and protocols that power the Ixian ecosystem."
          href="/docs/architecture"
          icon={<IoArrowForwardCircleOutline size={48} />}
        />
        <CustomCard
          title="Reference"
          description="Detailed technical specifications for the Ixian Platform's core components, and APIs."
          href="/docs/reference"
          icon={<IoArrowForwardCircleOutline size={48} />}
        />
      </div>
    </div>
  )
}
